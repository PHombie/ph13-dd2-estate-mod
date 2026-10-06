using System.Collections.Generic;
using System.Text;
using DD2Estate.Dd1;
using DD2Estate.UI;
using UnityEngine;
using UnityEngine.UI;

namespace DD2Estate.Estate
{
    /// <summary>
    /// The Nomad Wagon's screen, after DD1's: the wagon's backdrop with its keeper, and on the table
    /// (inventory_grid_background.png) this week's trinkets as DD1 item cards, six to a row, each with its
    /// price under it as DD1 writes one. A click buys; what is bought is gone from the table, as from any DD1
    /// inventory, and the other wares keep their places. DD1's wagon is the table and nothing else: trinkets
    /// are sold where DD1 sells them, in the Trinket Inventory (<see cref="RealmInventoryPanel"/>). A card is
    /// DD1's card of the rarity with DD2's icon of the trinket on it; pointing at it shows DD2's item text in
    /// DD1's tooltip beside the card.
    ///
    /// Where things are comes from DD1's layout files (nomad_wagon.layout.darkest for the table and its grid,
    /// shared/inventory/inventory.layout.darkest for a card in its cell, its price and its tooltip,
    /// building.layout.darkest for the body of a building screen). tools/preview_windows.py draws the same
    /// layout offline: change one, change the other.
    /// </summary>
    internal class NomadWagonPanel : MonoBehaviour
    {
        private const string Art = UpgradeUi.BuildingsDir + "nomad_wagon/nomad_wagon";
        private const string TableArt = UpgradeUi.BuildingsDir + "nomad_wagon/inventory_grid_background.png";
        private const string LayoutFile = UpgradeUi.BuildingsDir + "nomad_wagon/nomad_wagon.layout.darkest";
        private const string ItemLayoutFile = "shared/inventory/inventory.layout.darkest";
        private const string HoverArt = "overlays/eqp_mouseover.png";
        private const string HoverArtUnavailable = "overlays/eqp_unavailable_mouseover.png";

        private static readonly Vector2 TableSize = new Vector2(684f, 360f); // inventory_grid_background.png
        private static readonly Vector2 HoverSize = new Vector2(118f, 187f); // overlays/eqp_mouseover.png
        private const float TooltipBox = 36f;           // what DD1's tooltip box adds to its text's width

        // A card that waits for DD2's icon of its trinket, and the colour the icon takes once it is there.
        private class IconWanted
        {
            public Image Image;
            public string Id;
            public Color Tint;
        }

        private RosterWindow _window;
        private RectTransform _frame, _wares, _prices;
        private readonly List<IconWanted> _iconsWanted = new List<IconWanted>();
        private Vector2 _tablePos, _gridStart, _gridPitch, _card, _cardOffset, _costOffset, _tipOffset;
        private float _tipWidth;
        private int _columns;
        private string _shown;
        private float _nextIcons;

        private static NomadWagonPanel _instance;

        public static bool IsOpen => _instance != null && RosterWindow.Current != null && RosterWindow.Current == _instance._window;

        public static void Open()
        {
            var window = RosterWindow.Open(NomadWagon.BuildingId, ActivityText.Building(NomadWagon.BuildingId), Art);
            if (window == null) return;
            var panel = window.gameObject.AddComponent<NomadWagonPanel>();
            _instance = panel;
            panel._window = window;
            panel.Build(window.Frame);
            window.Refresh = panel.Refresh;
            panel.Refresh();
        }

        public static void Close()
        {
            if (IsOpen) RosterWindow.Close();
        }

        private void OnEnable()
        {
            NomadWagon.Changed += Redraw;
            UpgradeRules.Changed += Redraw;     // a bigger wagon, a better price
        }

        private void OnDisable()
        {
            NomadWagon.Changed -= Redraw;
            UpgradeRules.Changed -= Redraw;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        // ---- construction --------------------------------------------------------------------------------

        private void Build(RectTransform frame)
        {
            _frame = frame;
            var layout = Dd1Ui.Layout(LayoutFile);
            var items = Dd1Ui.Layout(ItemLayoutFile);
            // FALLBACK numbers: DD1's own values of these entries, used when a file cannot be read.
            _tablePos = _window.Body + Dd1Ui.Offset(layout, "inventory_system_background_layout", "pos", 230f, 150f);
            const string grid = "inventory_system_grid_layout", item = "inventory_item_layout", tip = "inventory_item_tooltip_layout";
            _gridStart = _tablePos + Dd1Ui.Offset(layout, grid, "start_pos", 55f, -10f);
            _gridPitch = Dd1Ui.Offset(layout, grid, "offset", 100f, 180f);
            _columns = Mathf.Max(1, Mathf.RoundToInt(Dd1Ui.Number(layout, grid, "number_of_columns", 6f)));
            // A card in its cell, the price under it and the tooltip beside it: all counted from the cell's corner.
            _card = Dd1Ui.Offset(items, item, "icon_size", 72f, 144f);
            _cardOffset = Dd1Ui.Offset(items, item, "icon_offset", 4f, 0f);
            _costOffset = Dd1Ui.Offset(items, item, "cost_offset", 37f, 157f);
            _tipOffset = Dd1Ui.Offset(items, tip, "offset", 85f, 0f);
            _tipWidth = Dd1Ui.Number(items, tip, "text_width", 200f);

            Dd1Ui.Art("Table", frame, TableArt, _tablePos, TableSize, new Color(0.05f, 0.045f, 0.04f, 0.9f));
            _wares = UiKit.Stretch(UiKit.Rect("Wares", frame));
            // The prices lie over the cards: the frame of the card under the pointer reaches down to them.
            _prices = UiKit.Stretch(UiKit.Rect("Prices", frame));
            if (_window.Upgrades != null) _window.Upgrades.transform.SetAsLastSibling();
        }

        private void Redraw()
        {
            _shown = null;
            Refresh();
        }

        // ---- state ---------------------------------------------------------------------------------------

        private void Refresh()
        {
            if (_frame == null) return;
            LoadIcons();

            var stock = NomadWagon.Current();

            // Everything the cards show, so they are only rebuilt when something changed.
            var key = new StringBuilder().Append(EstateState.Gold).Append('|').Append(NomadWagon.Discount);
            foreach (var ware in stock) key.Append('|').Append(ware.Id).Append(ware.Sold ? 's' : '-').Append(NomadWagon.BuyBlockReason(ware));
            if (key.ToString() == _shown) return;
            _shown = key.ToString();

            _window.Tooltip.Hide(null);
            UpgradeUi.Clear(_wares);
            UpgradeUi.Clear(_prices);
            _iconsWanted.Clear();

            // A ware that was bought has left the table; the others stay in the cells they came in.
            for (var i = 0; i < stock.Count; i++)
                if (!stock[i].Sold) AddWare(stock[i], i, _gridStart + new Vector2(_gridPitch.x * (i % _columns), _gridPitch.y * (i / _columns)));
            LoadIcons();
        }

        // DD1's card of the rarity with DD2's icon of the trinket in its middle, DD1's price under it.
        private void AddWare(NomadWagon.Ware ware, int index, Vector2 cell)
        {
            var blocked = NomadWagon.BuyBlockReason(ware);
            var price = NomadWagon.Price(ware);
            var at = cell + _cardOffset;
            // DD1 takes the colour out of what cannot be had (inventory_unselectable); here it is darkened.
            var tint = blocked != null ? Dd1Fonts.Colour("inventory_unselectable", new Color(0.4f, 0.4f, 0.4f)) : Color.white;

            var card = UpgradeUi.Art("Ware" + index, _wares, Trinkets.RarityArt(ware.Rarity), at, _card, new Color(0.1f, 0.09f, 0.08f, 0.95f));
            card.color *= tint;
            var icon = TrinketPicture.Add(card.transform);
            _iconsWanted.Add(new IconWanted { Image = icon, Id = ware.Id, Tint = tint });

            // The card under the pointer wears DD1's frame, as in every DD1 inventory.
            var hoverSprite = Dd1Ui.Sprite(blocked != null ? HoverArtUnavailable : HoverArt);
            var hover = UiKit.Image("Hover", card.transform, hoverSprite, hoverSprite != null ? Color.white : new Color(0.86f, 0.71f, 0.36f, 0.25f));
            ((RectTransform)hover.transform).Place(Dd1Ui.Middle, Dd1Ui.Middle, Vector2.zero, HoverSize);
            hover.gameObject.SetActive(false);

            UpgradeUi.BuildGoldPrice("Price" + index, _prices, cell + _costOffset, price, price > EstateState.Gold);

            UpgradeUi.Pointer(card, () => NomadWagon.Buy(index), RosterWindow.Close,
                () =>
                {
                    hover.gameObject.SetActive(true);
                    card.transform.SetAsLastSibling();      // the frame is larger than the card: no neighbour lies over it
                    _window.Tooltip.ShowAt(card, TooltipAt(cell), Trinkets.Name(ware.Id), Body(ware.Id, ware.Rarity, blocked), _tipWidth);
                },
                () =>
                {
                    hover.gameObject.SetActive(false);
                    _window.Tooltip.Hide(card);
                });
        }

        // Beside the card, level with its top (inventory_item_tooltip_layout). DD1's screen has room right of
        // the table's last cards, where the roster stands; this window ends there, so the box goes to the
        // card's other side, as far from it.
        private Vector2 TooltipAt(Vector2 cell)
        {
            var at = cell + _tipOffset;
            var box = _tipWidth + TooltipBox;
            if (at.x + box <= RosterWindow.FrameWidth) return at;
            var gap = _tipOffset.x - _cardOffset.x - _card.x;
            return new Vector2(cell.x + _cardOffset.x - gap - box, at.y);
        }

        // Under the trinket's name: its DD1 rarity in DD1's colour of the rarity, what DD2 says the item does,
        // and why the estate cannot have it, where it cannot.
        private static string Body(string id, string rarity, string blocked)
        {
            var text = "<color=#" + ColorUtility.ToHtmlStringRGB(Dd1Fonts.Colour(rarity, UiKit.Notable)) + ">" + QuestMapText.TrinketRarity(rarity) + "</color>";
            var description = Trinkets.Description(id);
            if (description.Length > 0) text += "\n" + description;
            return blocked != null ? text + "\n" + Dd1Ui.Tint(blocked + ".", "harmful") : text;
        }

        // DD2 loads item icons on demand: the cards ask until theirs has come.
        private void LoadIcons()
        {
            for (var i = _iconsWanted.Count - 1; i >= 0; i--)
            {
                var wanted = _iconsWanted[i];
                if (wanted.Image == null)
                {
                    _iconsWanted.RemoveAt(i);
                    continue;
                }
                var sprite = Trinkets.Icon(wanted.Id);
                if (sprite == null)
                {
                    if (Trinkets.HasNoIcon(wanted.Id)) _iconsWanted.RemoveAt(i);
                    continue;
                }
                wanted.Image.sprite = sprite;
                wanted.Image.color = wanted.Tint;
                _iconsWanted.RemoveAt(i);
            }
        }

        private void Update()
        {
            // icons come a few frames after they are asked for; the window's own refresh is only twice a second
            if (_iconsWanted.Count == 0 || Time.unscaledTime < _nextIcons) return;
            _nextIcons = Time.unscaledTime + 0.1f;
            LoadIcons();
        }

        // ---- dev bridge ----------------------------------------------------------------------------------

        public static object Snapshot()
        {
            if (!IsOpen) return new { open = false };
            return new
            {
                open = true, upgrades = _instance._window.Upgrades != null && _instance._window.Upgrades.IsOpen,
                message = _instance._window.Said, iconsLoading = _instance._iconsWanted.Count,
                table = new { x = _instance._tablePos.x, y = _instance._tablePos.y }, firstCard = new { x = _instance._gridStart.x + _instance._cardOffset.x, y = _instance._gridStart.y + _instance._cardOffset.y },
                columns = _instance._columns
            };
        }

        /// <summary>Dev bridge: show or hide the upgrade pane of the open window.</summary>
        public static bool ShowUpgrades(bool show)
        {
            if (!IsOpen || _instance._window.Upgrades == null) return false;
            _instance._window.Upgrades.SetOpen(show);
            return true;
        }
    }
}
