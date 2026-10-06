using System.Text;
using Assets.Code.Actor;
using DD2Estate.UI;
using TMPro;
using UnityEngine;

namespace DD2Estate.Estate
{
    /// <summary>
    /// The blacksmith's screen, after DD1's (<see cref="HeroActionFrame"/>: the class's picture, the hero's slot
    /// and name in the banner, DD1's lines about the class's arms in the text column). In the body DD1's two
    /// boxes (blacksmith.frame.png), weapon over armour, each with the hero's own piece as DD1 paints it for
    /// the class at its level (heroes/&lt;class&gt;/icons_equip/eqp_weapon_&lt;n&gt;.png, a card of 72x144) and one step
    /// per level to buy (blacksmith.layout.darkest: frame_pos, equipment_pos, equipment_spacing; a step every
    /// 75 pixels), the price under the step that can be bought next, as DD1 writes it. Click the open step to
    /// buy the level; pointing at the piece says what it is called and what it gives, pointing at a step what
    /// that level is called, what it gives and what DD1 asks for it (the smithy's level, the hero's resolve).
    /// A class DD1 never had keeps the smithy's own picture of a weapon and an armour.
    /// tools/preview_windows.py draws the same layout offline: change one, change the other.
    /// </summary>
    internal class BlacksmithPanel : MonoBehaviour
    {
        private const string Art = UpgradeUi.BuildingsDir + "blacksmith/blacksmith";
        private const string LayoutFile = Art + ".layout.darkest";
        private static readonly Vector2 FrameSize = new Vector2(467f, 360f);      // blacksmith.frame.png
        private static readonly Vector2 CardSize = new Vector2(72f, 144f);        // eqp_weapon_<n>.png, eqp_armour_<n>.png
        private static readonly Vector2 StandInSize = new Vector2(72f, 72f);      // blacksmith.weapon.icon.png, blacksmith.armour.icon.png

        private RosterWindow _window;
        private HeroActionFrame _heroes;
        private RectTransform _links, _list;
        private Vector2 _piecePos, _pieceSpacing, _pieceTip, _stepSpacing, _stepTip;
        private float _stepTipWidth;
        private bool _stepTipAuto;
        private string _shown;

        public static void Open()
        {
            var window = RosterWindow.Open(Blacksmith.BuildingId, ActivityText.Building(Blacksmith.BuildingId), Art);
            if (window == null) return;
            var panel = window.gameObject.AddComponent<BlacksmithPanel>();
            panel._window = window;
            panel.Build(window.Frame);
            window.Refresh = panel.Refresh;
            panel.Refresh();
        }

        private void Build(RectTransform frame)
        {
            _heroes = new HeroActionFrame(_window, Blacksmith.BuildingId) { Picked = Redraw };
            // FALLBACK numbers: DD1's own values of these entries, used when the file cannot be read.
            var layout = Dd1Ui.Layout(LayoutFile);
            const string tree = "blacksmith_upgrade_tree_layout", tip = "blacksmith_upgrade_requirement_tooltip_layout";
            _piecePos = _heroes.BodyPos + Dd1Ui.Offset(layout, "blacksmith_layout", "equipment_pos", 50f, 20f);
            _pieceSpacing = Dd1Ui.Offset(layout, "blacksmith_layout", "equipment_spacing", 0f, 176f);
            _pieceTip = Dd1Ui.Offset(layout, tree, "icon_tooltip_offset", 90f, 15f);
            _stepSpacing = Dd1Ui.Offset(layout, tree, "requirement_spacing", 75f, 0f);
            _stepTip = Dd1Ui.Offset(layout, tip, "tooltip_offset", 110f, 0f);
            _stepTipWidth = Dd1Ui.Number(layout, tip, "tooltip_text_width", 220f);
            // tooltip_is_auto_width 0: a step's tooltip is that wide whatever it says.
            _stepTipAuto = Dd1Ui.Number(layout, tip, "tooltip_is_auto_width", 0f) != 0f;

            Dd1Ui.Art("Boxes", frame, Art + ".frame.png", _heroes.BodyPos + Dd1Ui.Offset(layout, "blacksmith_layout", "frame_pos", 30f, 0f), FrameSize);
            // Under the pieces and their steps: the gold links of the bought steps show in the gaps only.
            _links = UiKit.Rect("StepLinks", frame).Stretch();
            _list = UiKit.Rect("Pieces", frame).Stretch();
            if (_window.Upgrades != null) _window.Upgrades.transform.SetAsLastSibling();
        }

        private void OnEnable()
        {
            UpgradeRules.Changed += Refresh;      // a better forge opens the next step at once
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
                Show("nobody", null);
                return;
            }
            var guid = hero.ActorGuid;
            // The mod's own facts, for the slot's tooltip: DD1's screen has no place for them.
            _heroes.Describe("Resolve level " + Resolve.Level(guid) + "   " + Blacksmith.Name(Blacksmith.Gear.Weapon) + " " + UpgradeText.Roman(Blacksmith.Level(guid, Blacksmith.Gear.Weapon))
                             + "   " + Blacksmith.Name(Blacksmith.Gear.Armour) + " " + UpgradeText.Roman(Blacksmith.Level(guid, Blacksmith.Gear.Armour)));

            // Everything the two boxes show, so they are only rebuilt when something changed.
            var key = new StringBuilder().Append(guid).Append('|').Append(EstateState.Gold).Append('|').Append(Resolve.Level(guid));
            foreach (var gear in new[] { Blacksmith.Gear.Weapon, Blacksmith.Gear.Armour })
                key.Append('|').Append(Blacksmith.Level(guid, gear)).Append(':').Append(Blacksmith.Block(hero, gear));
            Show(key.ToString(), hero);
        }

        private void Show(string key, ActorInstance hero)
        {
            if (key == _shown) return;
            _shown = key;
            _window.Tooltip.Hide(null);
            UpgradeUi.Clear(_links);
            UpgradeUi.Clear(_list);
            if (hero == null) return;
            AddPiece(hero, Blacksmith.Gear.Weapon, 0);
            AddPiece(hero, Blacksmith.Gear.Armour, 1);
        }

        private void AddPiece(ActorInstance hero, Blacksmith.Gear gear, int row)
        {
            var guid = hero.ActorGuid;
            var at = _piecePos + _pieceSpacing * row;
            var id = Blacksmith.Name(gear);
            var level = Blacksmith.Level(guid, gear);
            var most = Blacksmith.MaxLevel(hero, gear);
            var next = Blacksmith.Next(hero, gear);
            var wants = next != null ? Blacksmith.Wants(hero, next) : null;
            var price = next != null ? Blacksmith.Price(next, gear) : 0;

            // The hero's own piece as it is now: DD1's card of the class at this level. A class DD1 never
            // had gets the smithy's picture of the kind of piece instead.
            var card = Blacksmith.Card(hero, gear, level);
            var icon = card != null
                ? Dd1Ui.Art("Card." + id, _list, card, at, CardSize, null, true)
                : Dd1Ui.Art("Icon." + id, _list, Art + "." + Blacksmith.Tag(gear) + ".icon.png", at, StandInSize, new Color(0.12f, 0.12f, 0.12f), true);
            UpgradeUi.Pointer(icon, null, () => UpgradeUi.ShowSheet(guid),
                () => _window.Tip(icon, true, at + _pieceTip, Blacksmith.PieceName(hero, gear, level), Blacksmith.Effect(gear, level), _stepTipWidth, !_stepTipAuto),
                () => _window.Tip(icon, false, Vector2.zero, null, null));

            // The piece holds the first place of the row (level 1); the levels to buy follow it.
            var stepIcon = UpgradeUi.StepOffset("icon_offset", 40f, 10f);
            var stepCost = UpgradeUi.StepOffset("cost_offset", 62f, 72f);
            var stepFree = UpgradeUi.StepOffset("free_offset", 62f, 72f);
            for (var step = 2; step <= most; step++)
            {
                var rank = step;
                var corner = at + _stepSpacing * (step - 1);
                var state = step <= level ? UpgradeUi.NodeState.Bought : step == level + 1 && wants == null ? UpgradeUi.NodeState.Open : UpgradeUi.NodeState.Locked;
                var node = UpgradeUi.BuildNode(id + step, _list, corner + stepIcon, _links);
                node.Set(state);
                // What the level gives, and for one not made yet what DD1 asks for it, in DD1's words.
                string StepText()
                {
                    var text = Blacksmith.Effect(gear, rank);
                    var asks = rank > level ? HeroActionUi.Prerequisites(Blacksmith.Terms(hero, gear, rank), guid) : null;
                    return asks != null ? text + "\n" + asks : text;
                }
                UpgradeUi.Pointer(node.Icon,
                    step == level + 1 ? () => Act(Blacksmith.Fit(guid, gear), hero.ActorName + "'s " + id.ToLowerInvariant() + " is now level " + UpgradeText.Roman(level + 1) + ".") : (System.Action)null,
                    () => UpgradeUi.ShowSheet(guid),
                    () =>
                    {
                        // DD1's overlay of the step under the pointer
                        node.Highlight.gameObject.SetActive(true);
                        _window.Tooltip.ShowAt(node, corner + _stepTip, Blacksmith.PieceName(hero, gear, rank), StepText(), _stepTipWidth, _stepTipAuto);
                    },
                    () =>
                    {
                        node.Highlight.gameObject.SetActive(false);
                        _window.Tip(node, false, Vector2.zero, null, null);
                    });

                // DD1 writes a step's price under it; here under the one that can be bought next.
                if (step != level + 1 || next == null) continue;
                if (price > 0)
                {
                    var cost = UpgradeUi.BuildGoldPrice("Price." + id, _list, corner + stepCost, price, wants == null && price > EstateState.Gold);
                    if (wants != null)
                        foreach (var label in cost.GetComponentsInChildren<TextMeshProUGUI>()) label.color = UpgradeUi.Dim;
                }
                else
                {
                    // "Free" stands where the amount of a price would: its line begins 14 px above the offset (estate_currency_gold_layout).
                    var free = Dd1Ui.Line("Free." + id, _list, "town_free", corner + stepFree - new Vector2(0f, 14f), new Vector2(70f, 26f), TextAlignmentOptions.Top);
                    free.text = WindowText.Plain("town_free") ?? "Free";
                }
            }
        }

        private void Act(string trouble, string done)
        {
            _window.Say(trouble != null ? trouble + "." : done, trouble != null);
            Redraw();
        }
    }
}
