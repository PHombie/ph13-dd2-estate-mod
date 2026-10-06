using System;
using System.Collections.Generic;
using System.Text;
using DD2Estate.Dd1;
using DD2Estate.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD2Estate.Estate
{
    /// <summary>
    /// A building's upgrades, laid out like DD1's upgrade view with the art of the player's DD1 install: the
    /// pane (blgupgradebg.png) over the keeper's side of the building screen, what DD1 says upgrading the
    /// building does (building_verbose_&lt;id&gt;), "Upgraded:" and the share built in its banner, and one row per
    /// tree: title, the tree's icon and its steps (bought, open, locked), with DD1's dark plate
    /// (blg_townupgrade_costframe.png) round the step that comes next and its heirloom price. Pointing at a step
    /// or a tree's icon shows DD1's tooltip beside it, clicking an open step builds it. The "+" of the building
    /// screen opens and closes the pane (<see cref="RosterWindow"/>); the estate's bar below keeps the heirlooms
    /// in view.
    ///
    /// The pane lies over the screen as it is and replaces nothing of it: its art is black at three parts in
    /// four, so the keeper shows through it; the notch at its top left frames the building's icon and the art
    /// is clear over the screen's name (<see cref="Dd1ScreenName"/>).
    ///
    /// The pane is a child of the building's 1395x776 backdrop and is placed in its pixels. DD1's own numbers
    /// (campaign/town/buildings/building.layout.darkest and upgrade/upgrade.layout.darkest, screen pixels with
    /// the backdrop at 144,132) are read from the files; the stock values stand in if a file cannot be read.
    /// tools/preview_windows.py draws the same layout offline: change one, change the other.
    /// </summary>
    internal class UpgradePane : MonoBehaviour
    {
        // Where DD1 puts the backdrop on its screen: its layout files count from the screen's corner.
        private static readonly Vector2 Dd1AreaPos = new Vector2(144f, 132f);
        private const float VerboseHeight = 150f;       // from DD1's verbose_offset down to the first tree's title
        private const float DividerWidth = 620f;        // tree_divider_large.png
        private const float TreeIconWidth = 72f;        // <tree>.icon.png
        // DD1's pane has room for three trees. A building with more (a tree of the mod's own) puts two short
        // ones side by side. DD1's tree keeps DD1's places to the pixel; the other one begins where that one
        // ends, and its steps follow its icon closely, or they would not fit inside the pane.
        private const int Rows = 3;
        private const int ShortTree = 3;
        private const float SecondTree = 320f;
        private const float SecondTreeGap = 8f;

        private class StepView
        {
            public int Index;
            public UpgradeUi.Node Node;
            public Vector2 Corner;
            public RectTransform Costs;
            public string CostKey;
        }

        private class TreeView
        {
            public UpgradeRules.Tree Tree;
            public readonly List<StepView> Steps = new List<StepView>();
            public RectTransform Plate;
        }

        private readonly List<TreeView> _trees = new List<TreeView>();
        private string _building;
        private GameObject _body;
        private Transform _costs;
        private TextMeshProUGUI _percent;
        private Action<bool> _onToggled;
        private bool _fixedOpen;
        private TreeView _hoverTree;
        private int _hoverStep = -1;
        private string _lastText;
        private Vector2 _stepCost, _stepFree, _stepPlate, _tooltipOffset, _lockedTooltipOffset;
        private float _tooltipWidth, _lockedTooltipWidth;
        private bool _tooltipFixed, _lockedTooltipFixed;
        private float _nextRefresh;

        public bool IsOpen => _body != null && _body.activeSelf;

        /// <summary>
        /// Adds the pane (closed) to a building's backdrop. Null when the building has no upgrade trees.
        /// <paramref name="onToggled"/> lets the screen follow the pane (its "+" turns into DD1's cross).
        /// <paramref name="fixedOpen"/>: the pane is all the window shows.
        /// </summary>
        public static UpgradePane Attach(RectTransform art, string building, Action<bool> onToggled, bool fixedOpen = false)
        {
            var trees = UpgradeRules.TreesOf(building);
            if (trees.Count == 0) return null;
            var root = UiKit.Rect("Upgrades", art);
            root.Stretch();
            var pane = root.gameObject.AddComponent<UpgradePane>();
            pane._building = building;
            pane._onToggled = onToggled;
            pane._fixedOpen = fixedOpen;
            pane.Build(root, trees);
            pane._body.SetActive(false);
            pane.SetOpen(fixedOpen);
            return pane;
        }

        // ---- construction --------------------------------------------------------------------------------

        private void Build(RectTransform root, List<UpgradeRules.Tree> trees)
        {
            // FALLBACK numbers: DD1's own values of these entries, used when a file cannot be read.
            var layout = DarkestFile.Load(UpgradeUi.BuildingsDir + "building.layout.darkest");
            Vector2 Base(string block, string key, float x, float y) => UpgradeUi.Offset(layout, block, key, x, y);
            const string pane = "building_base_upgrade_layout", row = "building_base_upgrade_tree_layout", tip = "building_upgrade_requirement_tooltip_layout";
            const string lockedTip = "upgrade_locked_upgrade_requirement_tooltip_layout";

            var origin = Base("building_base_layout", "upgrade_base_pos", 172f, 259f) - Dd1AreaPos;
            var frame = origin + Base(pane, "frame_offset", -18f, -115f);
            var verbose = origin + Base(pane, "verbose_offset", 20f, 30f);
            var verboseWidth = Dd1Ui.Number(layout, pane, "verbose_width", 380f);
            var titlePos = origin + Base(pane, "upgrade_title_offset", 458f, 36f);
            var percentPos = origin + Base(pane, "upgrade_percent_offset", 480f, 62f);
            var first = origin + Base(pane, "upgrade_trees_offset", 0f, 195f);
            var spacing = Base(pane, "upgrade_trees_spacing", 0f, 160f);
            var treeTitle = Base(row, "title_offset", 20f, -35f);
            var treeIcon = Base(row, "icon_offset", 30f, 0f);
            var treeTip = Base(row, "icon_tooltip_offset", 30f, 106f);
            var stepStart = Base(row, "requirement_start_offset", 0f, 0f);
            var stepSpacing = Base(row, "requirement_spacing", 70f, 0f);
            var divider = Base(row, "divider_offset", 0f, 118f);
            var stepIcon = UpgradeUi.StepOffset("icon_offset", 40f, 10f);
            _stepCost = UpgradeUi.StepOffset("cost_offset", 62f, 72f);
            _stepFree = UpgradeUi.StepOffset("free_offset", 62f, 72f);
            _stepPlate = UpgradeUi.StepOffset("outline_offset", 13f, -12f);
            // A step that can be built, or is: a box of one width. A locked one: DD1's narrower box, as wide as its words.
            _tooltipOffset = Base(tip, "tooltip_offset", 130f, 0f);
            _tooltipWidth = Dd1Ui.Number(layout, tip, "tooltip_text_width", 300f);
            _tooltipFixed = Dd1Ui.Number(layout, tip, "tooltip_is_auto_width", 0f) < 0.5f;
            var steps = UpgradeUi.StepLayout;
            _lockedTooltipOffset = UpgradeUi.Offset(steps, lockedTip, "tooltip_offset", 110f, 0f);
            _lockedTooltipWidth = Dd1Ui.Number(steps, lockedTip, "tooltip_text_width", 250f);
            _lockedTooltipFixed = Dd1Ui.Number(steps, lockedTip, "tooltip_is_auto_width", 1f) < 0.5f;

            _body = UiKit.Rect("Body", root).Stretch().gameObject;
            var body = _body.transform;

            // The pane takes the clicks of whatever it covers; a right click on it puts it away (DD1: back out).
            var back = UpgradeUi.Art("Pane", body, UpgradeUi.BuildingsDir + "blgupgradebg.png", frame, new Vector2(662f, 764f), new Color(0.03f, 0.03f, 0.03f, 0.9f));
            UpgradeUi.Pointer(back, null, () => SetOpen(false));

            var words = Dd1Ui.Block("Verbose", body, "town_building_upgrade_verbose", verbose, new Vector2(verboseWidth, VerboseHeight));
            words.text = WindowText.Plain("building_verbose_" + _building) ?? "";
            Dd1Ui.Line("Upgraded", body, "town_building_upgrade_title", titlePos, new Vector2(150f, 30f)).text = WindowText.Plain("building_upgrade_title") ?? "Upgraded:";
            _percent = Dd1Ui.Line("Percent", body, "town_building_upgrade_percent", percentPos, new Vector2(130f, 64f));

            // the steps' connectors and the plates of the steps that come next: over the pane's backing, under
            // the trees' icons and the steps
            var links = UiKit.Rect("StepLinks", body).Stretch();
            var plates = UiKit.Rect("StepPlates", body).Stretch();
            var places = Places(trees);
            for (var i = 0; i < trees.Count; i++)
            {
                var tree = trees[i];
                var view = new TreeView { Tree = tree };
                _trees.Add(view);
                var place = places[i];
                var rowStart = first + spacing * place.Row;
                var at = rowStart + new Vector2(SecondTree * place.Column, 0f);
                var width = place.Shared ? SecondTree - 10f : DividerWidth;

                Dd1Ui.Line("Tree." + tree.Id, body, "town_upgrade_tree_title", at + treeTitle, new Vector2(width - treeTitle.x, 30f)).text = UpgradeText.TreeName(tree.Id);
                var icon = UpgradeUi.Art("Icon." + tree.Id, body, UpgradeText.Icon(tree), at + treeIcon);
                var iconTip = at + treeTip;
                UpgradeUi.Pointer(icon, null, () => SetOpen(false),
                    () => Window?.Tip(icon, true, iconTip, UpgradeText.TreeName(tree.Id), UpgradeText.TreeDescription(tree.Id), _tooltipWidth, _tooltipFixed),
                    () => Window?.Tip(icon, false, Vector2.zero, null, null));
                if (place.Column == 0) UpgradeUi.Art("Rule." + tree.Id, body, UpgradeUi.NodeDir + "tree_divider_large.png", rowStart + divider);

                // DD1: a tree's steps begin at the right edge of its icon. A step's place (upgrade_requirement_layout
                // base_size 102 72) holds its icon 40 px in, and its link, 50 px long from the place's left edge,
                // then bridges the gap between the tree's icon and the first step that is built.
                var iconWidth = icon.sprite != null ? icon.sprite.rect.width : TreeIconWidth;
                var stepsAt = at + new Vector2(treeIcon.x + iconWidth, 0f) + stepStart;
                if (place.Column > 0) stepsAt.x -= stepIcon.x - SecondTreeGap;
                for (var k = 0; k < tree.Steps.Count; k++)
                {
                    var corner = stepsAt + stepSpacing * k;
                    var step = new StepView { Index = k, Corner = corner, Node = UpgradeUi.BuildNode("Step." + tree.Id + "." + tree.Steps[k].Code, body, corner + stepIcon, links) };
                    view.Steps.Add(step);
                    UpgradeUi.Pointer(step.Node.Icon, () => OnClicked(view, step), () => SetOpen(false), () => OnHover(view, step, true), () => OnHover(view, step, false));
                }
                view.Plate = (RectTransform)UpgradeUi.Art("Plate." + tree.Id, plates, UpgradeUi.BuildingsDir + "blg_townupgrade_costframe.png", stepsAt + _stepPlate).transform;
                view.Plate.gameObject.SetActive(false);
            }
            // Over the steps' backings: a bought step's backing is wider than its place in the row.
            _costs = UiKit.Rect("Costs", body).Stretch();
        }

        private struct Place
        {
            public int Row;
            public int Column;
            public bool Shared;
        }

        // One tree a row, as in DD1. When there are more trees than rows, short neighbours pair up from the
        // bottom until they fit.
        private static List<Place> Places(List<UpgradeRules.Tree> trees)
        {
            var rows = new List<List<int>>();
            for (var i = 0; i < trees.Count; i++) rows.Add(new List<int> { i });
            for (var r = rows.Count - 1; r > 0 && rows.Count > Rows; r--)
            {
                if (rows[r].Count != 1 || rows[r - 1].Count != 1) continue;
                if (trees[rows[r][0]].Steps.Count > ShortTree || trees[rows[r - 1][0]].Steps.Count > ShortTree) continue;
                rows[r - 1].Add(rows[r][0]);
                rows.RemoveAt(r);
                r--;
            }
            var places = new Place[trees.Count];
            for (var r = 0; r < rows.Count; r++)
                for (var c = 0; c < rows[r].Count; c++)
                    places[rows[r][c]] = new Place { Row = r, Column = c, Shared = rows[r].Count > 1 };
            return new List<Place>(places);
        }

        // The window the pane lives in: it lends its tooltip and its line of text.
        private RosterWindow Window => RosterWindow.Current != null && transform.IsChildOf(RosterWindow.Current.transform) ? RosterWindow.Current : null;

        // ---- state ---------------------------------------------------------------------------------------

        public void SetOpen(bool open)
        {
            if (_fixedOpen) open = true;
            var was = IsOpen;
            _body.SetActive(open);
            _hoverTree = null;
            _hoverStep = -1;
            if (open)
            {
                // The screen may have added its own things after the pane was attached.
                transform.SetAsLastSibling();
                Refresh();
            }
            if (was != open)
            {
                try { _onToggled?.Invoke(open); }
                catch (Exception e) { Plugin.Log.LogError("Upgrades: the " + _building + " screen failed to follow the pane: " + e); }
            }
        }

        private void OnEnable()
        {
            UpgradeRules.Changed += RefreshSoon;
            _nextRefresh = 0f;
        }

        private void OnDisable()
        {
            UpgradeRules.Changed -= RefreshSoon;
        }

        private void RefreshSoon()
        {
            _nextRefresh = 0f;
        }

        private void Update()
        {
            if (!IsOpen || Time.unscaledTime < _nextRefresh) return;
            Refresh();
        }

        private void Refresh()
        {
            _nextRefresh = Time.unscaledTime + 0.4f;
            _percent.text = Mathf.RoundToInt(UpgradeRules.Fraction(_building) * 100f) + "%";
            // what can be built is before the player's eyes: the building's exclamation mark has done its work
            UpgradeAlerts.Looked(_building);

            foreach (var view in _trees)
            {
                var level = UpgradeRules.Level(view.Tree);
                foreach (var step in view.Steps)
                {
                    var state = StateOf(view.Tree, step.Index, level);
                    step.Node.Linked = true;
                    step.Node.Set(state, view == _hoverTree && step.Index == _hoverStep);
                    ShowCosts(view.Tree, step, state, step.Index == level);
                }
                // DD1's plate stands round the step that comes next and its price, for as long as there is one.
                var next = level < view.Steps.Count ? view.Steps[level] : null;
                if (view.Plate.gameObject.activeSelf != (next != null)) view.Plate.gameObject.SetActive(next != null);
                if (next != null) view.Plate.PlaceTopLeft(next.Corner + _stepPlate, UpgradeUi.TopLeft, view.Plate.sizeDelta);
            }
        }

        // The price of the step that comes next, in a row under it as DD1 writes it (a row is wider than a step:
        // DD1 shows no price under the steps after it). What the estate is short of is red; a step that waits for
        // another tree shows its lock and no price (DD1's own screen: the Stage Coach's Experienced Recruits).
        private void ShowCosts(UpgradeRules.Tree tree, StepView step, UpgradeUi.NodeState state, bool next)
        {
            var costs = tree.Steps[step.Index].Costs;
            var show = next && state == UpgradeUi.NodeState.Open;
            var key = new StringBuilder().Append((int)state).Append(show ? '+' : '-');
            var currencies = new List<string>();
            var amounts = new List<int>();
            if (show)
            {
                foreach (var cost in costs)
                {
                    var amount = UpgradeRules.Amount(cost);
                    if (amount <= 0) continue;
                    currencies.Add(cost.Currency);
                    amounts.Add(amount);
                    key.Append('|').Append(cost.Currency).Append(amount).Append(UpgradeRules.Purse(cost.Currency) < amount ? '!' : ' ');
                }
                if (costs.Count > 0 && currencies.Count == 0) key.Append("|free");
            }
            if (key.ToString() == step.CostKey) return;
            step.CostKey = key.ToString();
            if (step.Costs != null) Destroy(step.Costs.gameObject);
            step.Costs = null;
            if (!show || costs.Count == 0) return;

            var name = "Cost." + tree.Id + "." + step.Index;
            if (currencies.Count == 0)
            {
                // A town event pays for the step: DD1 writes "Free" where the price would be.
                var free = Dd1Ui.Line(name, _costs, "town_free", step.Corner + _stepFree, new Vector2(70f, 26f), TextAlignmentOptions.Top);
                free.text = WindowText.Plain("town_free") ?? "Free";
                step.Costs = (RectTransform)free.transform;
                return;
            }
            var labels = UpgradeUi.BuildPrice(name, _costs, step.Corner + _stepCost, currencies, amounts, out var row);
            for (var i = 0; i < labels.Count; i++)
                labels[i].color = state == UpgradeUi.NodeState.Locked ? UpgradeUi.Dim
                    : UpgradeRules.Purse(currencies[i]) < amounts[i] ? Dd1Fonts.Colour("town_currency_cant_afford_amount", UiKit.Harmful) : Dd1Fonts.Colour("town_currency_amount", UiKit.Gold);
            if (state == UpgradeUi.NodeState.Locked)
                foreach (var image in row.GetComponentsInChildren<Image>()) image.color = new Color(1f, 1f, 1f, 0.55f);
            step.Costs = row;
        }

        // Open: the next step, with everything it needs from other trees built. Whether the estate can pay
        // shows in the price's colour, not in the icon.
        private static UpgradeUi.NodeState StateOf(UpgradeRules.Tree tree, int index, int level)
        {
            if (index < level) return UpgradeUi.NodeState.Bought;
            if (index > level) return UpgradeUi.NodeState.Locked;
            return UpgradeRules.Missing(tree.Steps[index]).Count == 0 ? UpgradeUi.NodeState.Open : UpgradeUi.NodeState.Locked;
        }

        // DD1's tooltip of a step: under the tree's name what the step brings, then "Prerequisites:" and, one a
        // line in DD1's colour for them, the steps that have to be built first ("Instructor Mastery Level 2").
        private static string Describe(UpgradeRules.Tree tree, int index)
        {
            var text = new StringBuilder();
            foreach (var line in UpgradeText.StepLines(tree, index + 1))
            {
                if (text.Length > 0) text.Append('\n');
                text.Append(line);
            }
            var waits = UpgradeText.Prerequisites(tree, index);
            if (waits.Count == 0) return text.ToString();
            if (text.Length > 0) text.Append('\n');
            text.Append(WindowText.Plain("upgrade_prerequisite_tooltip_title") ?? "Prerequisites:");
            foreach (var wait in waits) text.Append('\n').Append(Dd1Ui.Tint(wait, "upgrade_tree_prerequisite_not_purchased"));
            return text.ToString();
        }

        // ---- input ---------------------------------------------------------------------------------------

        private void OnHover(TreeView view, StepView step, bool on)
        {
            if (on)
            {
                _hoverTree = view;
                _hoverStep = step.Index;
                ShowTip(view, step);
            }
            else if (_hoverTree == view && _hoverStep == step.Index)
            {
                _hoverTree = null;
                _hoverStep = -1;
                Window?.Tip(step, false, Vector2.zero, null, null);
            }
            Refresh();
        }

        private void ShowTip(TreeView view, StepView step)
        {
            var title = UpgradeText.TreeName(view.Tree.Id);
            var body = Describe(view.Tree, step.Index);
            _lastText = title + "\n" + body;
            var locked = StateOf(view.Tree, step.Index, UpgradeRules.Level(view.Tree)) == UpgradeUi.NodeState.Locked;
            Window?.Tip(step, true, step.Corner + (locked ? _lockedTooltipOffset : _tooltipOffset), title, body,
                locked ? _lockedTooltipWidth : _tooltipWidth, locked ? _lockedTooltipFixed : _tooltipFixed);
        }

        private void OnClicked(TreeView view, StepView step)
        {
            var level = UpgradeRules.Level(view.Tree);
            if (step.Index < level) return;
            string trouble;
            if (step.Index > level) trouble = "Build the step before it first";
            else trouble = UpgradeRules.Buy(view.Tree);
            var notice = trouble != null ? trouble + "." : UpgradeText.TreeName(view.Tree.Id) + " " + UpgradeText.Roman(step.Index + 1) + " is built.";
            _lastText = notice;
            Window?.Say(notice, trouble != null);
            Refresh();
            if (_hoverTree == view && _hoverStep == step.Index) ShowTip(view, step);
            _lastText = notice;
        }

        /// <summary>For tests: what the pane shows (the tooltip of the step last pointed at, or what the last click came to).</summary>
        public object Snapshot()
        {
            return new { building = _building, open = IsOpen, text = _lastText, percent = _percent != null ? _percent.text : null };
        }
    }
}
