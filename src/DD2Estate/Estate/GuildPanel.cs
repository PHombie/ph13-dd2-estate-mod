using System.Collections.Generic;
using System.Text;
using Assets.Code.Actor;
using DD2Estate.UI;
using UnityEngine;

namespace DD2Estate.Estate
{
    /// <summary>
    /// The guild's screen, after DD1's (<see cref="HeroActionFrame"/>: the class's picture, the hero's slot and
    /// name in the banner, DD1's lines about the class in the text column). The hero's skills stand in the
    /// body where guild.layout.darkest puts them (skill_pos, a row every skill_spacing): the skill's picture,
    /// DD1's padlock on it and its price at its foot while it is not learned (icon_cost_offset), DD1's mark
    /// around it while the hero takes it into a fight (shared/character/selected_ability.png), its step to
    /// the right (guild_upgrade_tree_layout: requirement_spacing) with the step's price under it, and DD1's
    /// rule under the row. As in DD1 a click on a padlocked picture learns the skill, a click on a learned
    /// one takes it into the hero's fights or out of them, and a click on an open step pays for it; pointing
    /// at a picture or a step says what it is and what DD1 asks for it. Right click for DD2's character sheet.
    ///
    /// The mod's own: DD1 has four ranks to buy for a skill and a DD2 skill has one better version, so a row
    /// has one step, "mastered"; and a DD2 hero has eleven skills where DD1's has seven, so a second column
    /// stands beside DD1's, each row's two skills over the two halves of DD1's rule.
    /// tools/preview_windows.py draws the same layout offline: change one, change the other.
    /// </summary>
    internal class GuildPanel : MonoBehaviour
    {
        private const string Art = UpgradeUi.BuildingsDir + "guild/guild";
        private const string LayoutFile = Art + ".layout.darkest";
        private const string Padlock = "shared/character/lockedskill.png";
        private const string InUse = "shared/character/selected_ability.png";    // 90x90: DD1's frame of a skill in use
        private const string Rule = UpgradeUi.NodeDir + "tree_divider_medium.png";

        private const float IconSize = 72f;             // a skill's picture
        private const float MarkSize = 90f;
        private const float RuleWidth = 400f;           // tree_divider_medium.png
        private const int Rows = 6;                     // DD1 has seven; the seventh would lie on the window's line of text
        private const float TipRoom = 150f;             // what a step's tooltip may need under a picture

        private RosterWindow _window;
        private HeroActionFrame _heroes;
        private RectTransform _links, _grid, _prices;
        private Vector2 _first, _spacing, _stepSpacing, _stepIcon, _stepCost, _cost, _locked, _divider, _iconTip, _stepTipAbove, _stepTipBelow;
        private float _columnPitch, _stepTipWidth;
        private string _shown;

        public static void Open()
        {
            var window = RosterWindow.Open(Guild.BuildingId, ActivityText.Building(Guild.BuildingId), Art);
            if (window == null) return;
            var panel = window.gameObject.AddComponent<GuildPanel>();
            panel._window = window;
            panel.Build(window.Frame);
            window.Refresh = panel.Refresh;
            panel.Refresh();
        }

        private void Build(RectTransform frame)
        {
            _heroes = new HeroActionFrame(_window, Guild.BuildingId) { Picked = Redraw };
            // FALLBACK numbers: DD1's own values of these entries, used when the file cannot be read.
            var layout = Dd1Ui.Layout(LayoutFile);
            const string tree = "guild_upgrade_tree_layout", tip = "guild_upgrade_requirement_tooltip_layout";
            _first = _heroes.BodyPos + Dd1Ui.Offset(layout, "guild_layout", "skill_pos", 44f, -4f);
            _spacing = Dd1Ui.Offset(layout, "guild_layout", "skill_spacing", 0f, 91f);
            _stepSpacing = Dd1Ui.Offset(layout, tree, "requirement_spacing", 75f, 0f);
            _cost = Dd1Ui.Offset(layout, tree, "icon_cost_offset", 34f, 70f);
            _locked = Dd1Ui.Offset(layout, tree, "icon_locked_offset", 0f, 0f);
            _divider = Dd1Ui.Offset(layout, tree, "divider_offset", 0f, 82f);
            _iconTip = Dd1Ui.Offset(layout, tree, "icon_tooltip_offset", 90f, 0f);
            // A step's tooltip counts from the skill's picture (tooltip_is_offset_from_tree_icon): under it, or over it.
            var stepTip = Dd1Ui.Offset(layout, tip, "tooltip_offset", 0f, 0f);
            _stepTipAbove = stepTip + Dd1Ui.Offset(layout, tree, "requirement_tooltip_tree_icon_above_offset", 0f, -10f);
            _stepTipBelow = stepTip + Dd1Ui.Offset(layout, tree, "requirement_tooltip_tree_icon_below_offset", 0f, 40f);
            _stepTipWidth = Dd1Ui.Number(layout, tip, "tooltip_text_width", 200f);
            // A step's 50 px icon and its price, as in every DD1 tree (upgrade.layout.darkest).
            _stepIcon = UpgradeUi.StepOffset("icon_offset", 40f, 10f);
            _stepCost = UpgradeUi.StepOffset("cost_offset", 62f, 72f);
            // DD1's rule runs 400 px under a row: the second column's skill stands over its right half.
            var rule = Dd1Ui.Sprite(Rule);
            _columnPitch = (rule != null ? rule.rect.width : RuleWidth) * 0.5f;

            // Under every cell: the gold links of the bought steps show in the gaps only.
            _links = UiKit.Rect("StepLinks", frame).Stretch();
            _grid = UiKit.Rect("Skills", frame).Stretch();
            // Over every cell: a price stands at the foot of its picture, where the mark of the row below begins.
            _prices = UiKit.Rect("Prices", frame).Stretch();
            if (_window.Upgrades != null) _window.Upgrades.transform.SetAsLastSibling();
        }

        private void OnEnable()
        {
            UpgradeRules.Changed += Refresh;      // a better guild opens the next mastery at once
        }

        private void OnDisable()
        {
            UpgradeRules.Changed -= Refresh;
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
            var skills = Guild.SkillsOf(hero);
            var mastered = 0;
            foreach (var skill in skills)
                if (skill.Mastered) mastered++;
            var limit = Guild.MasteryLimit(hero);
            var wants = Guild.MasteryWants(hero);
            // The mod's own facts, for the slot's tooltip: DD1's screen has no place for them.
            _heroes.Describe("Resolve level " + Resolve.Level(hero.ActorGuid) + "   Mastered " + mastered + " of " + (limit == int.MaxValue ? "any" : limit.ToString()),
                wants == null || skills.Count == 0 ? null : "Next mastery: " + wants);

            // Everything a cell shows, so the cells are only rebuilt when something changed.
            var key = new StringBuilder().Append(hero.ActorGuid).Append('|').Append(Guild.LearnPrice(hero)).Append('|').Append(Guild.MasterPrice(hero));
            key.Append('|').Append(EstateState.Gold).Append('|').Append(wants);
            foreach (var skill in skills) key.Append('|').Append(skill.Id).Append(skill.Known ? 'k' : '-').Append(skill.Mastered ? 'm' : '-').Append(skill.Equipped ? 'e' : '-');
            Show(key.ToString(), hero, skills);
        }

        private void Show(string key, ActorInstance hero, List<Guild.Skill> skills)
        {
            if (key == _shown) return;
            _shown = key;
            _window.Tooltip.Hide(null);
            UpgradeUi.Clear(_links);
            UpgradeUi.Clear(_grid);
            UpgradeUi.Clear(_prices);
            // DD1's Guild writes nothing on the window's line; a hero it can teach nothing is the one thing said there.
            _window.Hint(hero != null && skills.Count == 0 ? hero.ActorName + " fights their own way: the guild has nothing to teach them." : null);
            if (hero == null || skills.Count == 0) return;
            var learnPrice = Guild.LearnPrice(hero);
            var masterPrice = Guild.MasterPrice(hero);
            var wants = Guild.MasteryWants(hero);
            // DD1's rule under every row, drawn as its art is.
            for (var row = 0; row < Rows && row < skills.Count; row++)
                Dd1Ui.Art("Rule" + row, _grid, Rule, _first + _spacing * row + _divider);
            for (var i = 0; i < skills.Count; i++)
                AddCell(hero, skills[i], _first + new Vector2(_columnPitch * (i / Rows), 0f) + _spacing * (i % Rows), learnPrice, masterPrice, wants);
        }

        // wants: what still keeps another mastery shut (the guild's level, the hero's resolve); null when only its price is left.
        private void AddCell(ActorInstance hero, Guild.Skill skill, Vector2 at, int learnPrice, int masterPrice, string wants)
        {
            var guid = hero.ActorGuid;
            var sprite = Guild.Icon(skill.Id);
            var icon = UiKit.Image("Icon." + skill.Id, _grid, sprite, sprite == null ? new Color(0.2f, 0.2f, 0.2f) : Color.white, true);
            icon.preserveAspect = true;
            ((RectTransform)icon.transform).PlaceTopLeft(at, UpgradeUi.TopLeft, new Vector2(IconSize, IconSize));
            if (!skill.Known)
            {
                // DD1's look of what is not bought (upgrade_tree_icon_not_purchased: dark and without colour) under its padlock.
                if (sprite != null) Dd1Shade.Of("upgrade_tree_icon_not_purchased", 0.4f, 0f).Apply(icon);
                Dd1Ui.Art("Locked." + skill.Id, _grid, Padlock, at + _locked);
            }
            else if (skill.Equipped)
            {
                var around = (MarkSize - IconSize) * 0.5f;
                Dd1Ui.Art("InUse." + skill.Id, _grid, InUse, at - new Vector2(around, around), new Vector2(MarkSize, MarkSize));
            }

            // The step is open when only gold can be missing; anything else keeps it locked. DD1's first
            // purchase of a skill lies on the picture itself; the steps are what is bought after it.
            var canMaster = skill.Known && !skill.Mastered && skill.HasMastery && masterPrice > 0 && wants == null;
            var cell = at + _stepSpacing;
            var master = UpgradeUi.BuildNode("Mastered." + skill.Id, _grid, cell + _stepIcon, _links);
            master.Set(skill.Mastered ? UpgradeUi.NodeState.Bought : canMaster ? UpgradeUi.NodeState.Open : UpgradeUi.NodeState.Locked);

            // DD1 writes a skill's price at the foot of its picture and a step's under the step.
            if (!skill.Known && learnPrice > 0) UpgradeUi.BuildGoldPrice("Price." + skill.Id, _prices, at + _cost, learnPrice, learnPrice > EstateState.Gold);
            else if (canMaster) UpgradeUi.BuildGoldPrice("Price." + skill.Id, _prices, cell + _stepCost, masterPrice, masterPrice > EstateState.Gold);

            // DD1's words for what a purchase asks; the mod's for what a DD2 skill's one step is.
            string LearnText() => skill.Known ? null : HeroActionUi.Prerequisites(Guild.LearnTerms(hero), guid);
            string MasterText()
            {
                if (skill.Mastered) return "Mastered";
                if (!skill.HasMastery) return "This skill has no mastery.";
                var asks = HeroActionUi.Prerequisites(Guild.MasteryTerms(hero), guid);
                return "Mastery" + (asks != null ? "\n" + asks : "");
            }

            UpgradeUi.Pointer(icon,
                skill.Known
                    ? () => Act(Guild.ToggleEquipped(guid, skill.Id), null)
                    : (System.Action)(() => Act(Guild.Learn(guid, skill.Id), hero.ActorName + " learns " + skill.Name + ".")),
                () => UpgradeUi.ShowSheet(guid),
                () => _window.Tip(icon, true, at + _iconTip, skill.Name, LearnText()),
                () => _window.Tip(icon, false, Vector2.zero, null, null));
            UpgradeUi.Pointer(master.Icon, !skill.Known || skill.Mastered || !skill.HasMastery ? (System.Action)null : () => Act(Guild.Master(guid, skill.Id), hero.ActorName + " masters " + skill.Name + "."),
                () => UpgradeUi.ShowSheet(guid),
                () =>
                {
                    // DD1's overlay of the step under the pointer
                    master.Highlight.gameObject.SetActive(true);
                    // under the picture, or over it where the window's foot is near
                    if (at.y + IconSize + _stepTipBelow.y + TipRoom > RosterWindow.FrameHeight) _window.Tooltip.Show(master, at + _stepTipAbove, skill.Name, MasterText());
                    else _window.Tip(master, true, at + new Vector2(0f, IconSize) + _stepTipBelow, skill.Name, MasterText(), _stepTipWidth);
                },
                () =>
                {
                    master.Highlight.gameObject.SetActive(false);
                    _window.Tip(master, false, Vector2.zero, null, null);
                });
        }

        private void Act(string trouble, string done)
        {
            if (trouble != null) _window.Say(trouble + ".", true);
            else if (done != null) _window.Say(done);
            Redraw();
        }
    }
}
