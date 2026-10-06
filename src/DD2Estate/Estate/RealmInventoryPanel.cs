using System;
using System.Collections.Generic;
using System.Text;
using Assets.Code.UI.Managers;
using Assets.Code.Utils;
using DD2Estate.Dd1;
using DD2Estate.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DD2Estate.Estate
{
    /// <summary>
    /// DD1's Trinket Inventory, on DD1's layout (campaign/town/realm_inventory/realm_inventory.layout.darkest,
    /// its art beside it): the panel that stands against the roster column (town.layout.darkest
    /// realm_inventory_pos) with the estate's unworn trinkets in DD1's grid of seven, DD1's rules between the
    /// cards, its three sort buttons and "unequip all" beside the close button, and the line under the title
    /// where DD1 says how a trinket is sold. A card is DD1's card of the trinket's rarity with DD2's icon on
    /// it, 4 pixels into its cell (shared/inventory/inventory.layout.darkest), with DD1's sparkle running
    /// round an ancestral trinket and a trophy (fx/trinket_sparkle); pointing at it shows DD2's own text of
    /// the item in DD1's tooltip, beside the card where DD1's item tooltip stands.
    ///
    /// DD1 shows the panel beside its character sheet and trinkets are dragged between the two; opened from
    /// the bar alone it stands by itself. So here: the hero's sheet (DD2's own, with DD1's "Equipment" on it:
    /// <see cref="SheetEquipment"/>) and this panel are on screen together, the sheet moved aside to the
    /// panel's left for as long as both are up. The hero at hand is the sheet's hero. He comes from the roster
    /// column, as in every window of the hamlet: a click on a row (or a row dragged onto the panel) opens that
    /// hero's sheet beside the panel; the sheet's own arrows step on to the next one.
    ///
    /// A click on a card puts the trinket on the hero, a click on a worn one (on the sheet) takes it off; a
    /// card can be dragged onto one of the sheet's two slots (what is there changes places with it), onto the
    /// hero anywhere else on the sheet, or straight onto another hero's roster row, a worn one back onto the
    /// panel. Shift and a click sells, after DD1's question. Every move is DD2's own (<see cref="RealmInventory"/>).
    ///
    /// Which number of the layout means what is argued in docs/recon/dd1-town-panels.md.
    /// tools/preview_town_panels.py draws the same panel offline: change one, change the other.
    /// </summary>
    internal class RealmInventoryPanel : MonoBehaviour
    {
        private const string Dir = "campaign/town/realm_inventory/";
        private const string LayoutFile = Dir + "realm_inventory.layout.darkest";
        private const string TownLayout = "campaign/town/town.layout.darkest";
        private const string CurrencyLayout = "shared/estate/estate.layout.darkest";
        private const string ItemLayout = "shared/inventory/inventory.layout.darkest";
        private const string Sparkle = "fx/trinket_sparkle/trinket_sparkle.sprite";

        // Sizes of DD1's art.
        private static readonly Vector2 PanelSize = new Vector2(667f, 780f);     // realminv_bg.png
        private static readonly Vector2 Card = new Vector2(72f, 144f);           // panels/icons_equip/trinket/rarity_*.png
        private static readonly Vector2 SortSize = new Vector2(32f, 32f);        // realm_inventory_sort_*.png
        private static readonly Vector2 MarkSize = new Vector2(49f, 16f);        // realm_inventory_sort_current_overlay.png
        private static readonly Vector2 ArrowSize = new Vector2(62f, 49f);       // realm_inventory_uparrow.png
        private static readonly Vector2 CloseSize = new Vector2(32f, 32f);       // progression_close.png
        private static readonly Vector2 RowRule = new Vector2(628f, 12f);        // realminventory_h_grid.png
        private static readonly Vector2 ColumnRules = new Vector2(404f, 164f);   // realminventory_v_grid.png: six lines, 80 apart, the first 2 in
        private const float RailWidth = 21f;                                     // shared/widgets/scrollbarmid.png
        private const float CapArt = 7f;                                         // the drawn rows of scrollbartop.png
        // Measured from characterpanel_frames.png: the part of DD1's character sheet around its equipment band
        // is the hero sheet's to draw now (SheetEquipment); this panel stands beside the sheet, as DD1's does.
        private const float SayLasts = 6f;
        private const int RowsShown = 4;                    // realm_inventory_layout scroll_max_visible_rows

        private static RealmInventoryPanel _instance;
        private static RealmInventory.Order _order = RealmInventory.Order.Arrival;
        private static bool _descending;
        // DD1's "Always" was the answer to its question about a sale
        private static bool _sellUnasked;

        private TownPanel _panel;
        private RectTransform _inventory, _tips, _cardTips, _list;
        private ScrollRect _scroll;
        private Dd1Tooltip _tooltip, _cardTip;
        private Vector2 _itemOffset, _itemTip;
        private float _itemTipWidth, _sortTime, _sortedAt;
        private float _windowHeight;
        // Where every card of the stores stands, and from where the cards travel after a sort (sort_time).
        private readonly Dictionary<string, Vector2> _places = new Dictionary<string, Vector2>();
        private Dictionary<string, Vector2> _sortedFrom;
        private readonly List<KeyValuePair<RectTransform, Vector2[]>> _travelling = new List<KeyValuePair<RectTransform, Vector2[]>>();
        private TextMeshProUGUI _description, _sellAmount;
        private Image _sellIcon;
        private RectTransform _sortMark;
        private readonly Dictionary<RealmInventory.Order, RectTransform> _sortButtons = new Dictionary<RealmInventory.Order, RectTransform>();
        private Vector2 _markUp, _markDown, _gridAt, _pitch, _cellStart;
        private float _rulesLift, _rowsTop;
        private int _columns;
        // Every card whose picture DD2 has not handed over yet: this panel's and the hero sheet's (the cards are made
        // by the same code, and whoever shows some keeps asking: LoadIcons). A card that is gone drops out by itself.
        private static readonly List<KeyValuePair<Image, string>> _iconsWanted = new List<KeyValuePair<Image, string>>();
        // The hero at hand, and the hero of the sheet as it was last seen (0: no sheet up).
        private uint _guid, _sheetHero;
        private float _pickedAt = -10f;
        private const float SheetComes = 1f;
        private string _shown, _hovered, _said;
        private bool _saidTrouble, _closed;
        private float _saidUntil, _nextIcons;

        public static bool IsOpen => _instance != null && TownPanel.Current != null && TownPanel.Current == _instance._panel;

        public static void Toggle()
        {
            if (IsOpen) TownPanel.Close();
            else Open();
        }

        public static bool Open()
        {
            var panel = TownPanel.Open(RealmInventory.Id, false);
            if (panel == null) return false;
            var view = panel.gameObject.AddComponent<RealmInventoryPanel>();
            _instance = view;
            view._panel = panel;
            view.Build();
            panel.Refresh = view.Refresh;
            panel.Closed = view.OnClosed;
            view.Refresh();
            // DD1's sound of the trinket box (silent without DD1's audio)
            Dd2.EstateAudio.Ui("ui/town/trinket_open");
            return true;
        }

        public static void Close()
        {
            if (IsOpen) TownPanel.Close();
        }

        private void OnEnable()
        {
            RealmInventory.Changed += Redraw;
            NomadWagon.Changed += Redraw;       // a trinket sold, or bought at the wagon a moment ago
        }

        private void OnDisable()
        {
            RealmInventory.Changed -= Redraw;
            NomadWagon.Changed -= Redraw;
            if (!_closed) OnClosed();
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            if (_cardTips != null) Destroy(_cardTips.gameObject);
        }

        // The roster's rows go back to the party at once, and what was moved is saved once.
        private void OnClosed()
        {
            _closed = true;
            _guid = 0u;
            HookRoster(false);
            RealmInventory.Flush();
            // the cards' tooltip stands on the hamlet's canvas, not in the panel: it goes with it
            if (_cardTips != null) Destroy(_cardTips.gameObject);
            Dd2.EstateAudio.Ui("ui/town/trinket_close");
        }

        // ---- construction --------------------------------------------------------------------------------

        private void Build()
        {
            // FALLBACK numbers: DD1's own values of these entries, used when a file cannot be read.
            var town = Dd1Ui.Layout(TownLayout);
            var layout = Dd1Ui.Layout(LayoutFile);
            const string main = "realm_inventory_layout", grid = "realm_inventory_grid_layout";
            var at = Dd1Ui.Offset(town, "town_screen_layout", "realm_inventory_pos", 881f, 128f);

            _inventory = _panel.AddPart("Inventory", at, PanelSize, TownPanel.Side.Right);
            var back = Dd1Ui.Art("Back", _inventory, Dir + "realminv_bg.png", Vector2.zero, PanelSize, new Color(0.03f, 0.03f, 0.035f, 0.97f), true);
            TownPanel.RightClickCloses(back);
            var content = UiKit.Rect("Content", _inventory).PlaceTopLeft(Dd1Ui.Offset(layout, main, "content_offset", 0f, 2f), Dd1Ui.TopLeft, PanelSize);

            var closeAt = Dd1Ui.Offset(layout, main, "close_pos", 610f, 22f);
            Dd1Ui.ArtButton("Close", content, "shared/progression/progression_close.png", closeAt, CloseSize, TownPanel.Close, "X");

            // DD1's row of small buttons ends where sort_button_pos says, left of the close button: read the
            // other way (starting there, 42 apart) the second would lie on the close button and the third
            // outside the panel. Its order is in no file; DD1's own frame has it (the four pictures matched in
            // _lab/dd1_ref/town/estate_bar_3_trinket_inventory.png): unequip all, by class, by rarity, by name.
            var last = Dd1Ui.Offset(layout, main, "sort_button_pos", 567f, 22f);
            var step = Dd1Ui.Offset(layout, main, "sort_button_spacing", 42f, 0f);
            // DD1's name of a town screen: the chest's icon and "Trinket Inventory" beside it, up to the small buttons
            var namePos = Dd1Ui.Offset(layout, main, "name_pos", -44f, -10f);
            Dd1ScreenName.Add(content, namePos, Dir + "realm_inventory.icon.png", RealmInventory.ScreenName, null, Mathf.Max(120f, last.x - step.x * 3f - 8f - Dd1ScreenName.NameLeft(namePos)));
            var tipAt = Dd1Ui.Offset(layout, main, "sort_button_tooltip_offset", 16f, -12f);
            _markUp = Dd1Ui.Offset(layout, main, "sort_button_current_ascending_overlay_offset", -8f, -8f);
            _markDown = Dd1Ui.Offset(layout, main, "sort_button_current_descending_overlay_offset", -8f, 24f);
            Sort(content, last, tipAt, RealmInventory.Order.Name, "realm_inventory_sort_alphabetical.png", "str_sort_trinkets_alphabetically", "Sort by trinket name");
            Sort(content, last - step * 2f, tipAt, RealmInventory.Order.Class, "realm_inventory_sort_class.png", "str_sort_trinkets_by_class", "Sort by character class restriction");
            Sort(content, last - step, tipAt, RealmInventory.Order.Rarity, "realm_inventory_sort_rarity.png", "str_sort_trinkets_by_rarity", "Sort by rarity");
            var strip = Dd1Ui.ArtButton("UnequipAll", content, Dir + "realm_inventory_unequip_trinkets.png", last - step * 3f, SortSize, UnequipAll, "U");
            _sortTime = Dd1Ui.Number(layout, main, "sort_time", 0.4f);
            UiKit.Hover(strip.gameObject, inside => Tip(strip, inside, (RectTransform)strip.transform, tipAt, null, WindowText.Plain("str_unequip_all_trinkets") ?? "Unequip all trinkets", true));
            _sortMark = (RectTransform)Dd1Ui.Art("SortMark", content, Dir + "realm_inventory_sort_current_overlay.png", Vector2.zero, MarkSize, UiKit.Gold).transform;

            // Under the title: how a trinket is sold and, while Shift is held over one, what it sells for
            // (DD1's price widget: the coin and the number, shared/estate/estate.layout.darkest).
            var words = Dd1Ui.Offset(layout, main, "trinket_info_description", 150f, 100f);
            var value = Dd1Ui.Offset(layout, main, "trinket_sell_value", 365f, 116f);
            var currency = Dd1Ui.Layout(CurrencyLayout);
            var coin = value + Dd1Ui.Offset(currency, "estate_currency_gold_layout", "icon_offset", 0f, -12f);
            var number = value + Dd1Ui.Offset(currency, "estate_currency_gold_layout", "number_offset", 25f, -14f);
            _description = Dd1Ui.Line("Description", content, "realm_inventory_trinket_sell_description", words, new Vector2(PanelSize.x - words.x - 24f, 26f));
            _sellIcon = Dd1Ui.Art("SellCoin", content, UpgradeUi.GoldIcon, coin, new Vector2(24f, 24f));
            _sellAmount = Dd1Ui.Line("SellValue", content, "town_currency_amount", number, new Vector2(120f, 26f));

            // The grid. DD1's rules are part of it: a row rule grid_visuals_offset above every row, the six
            // column rules of a row lifted as much, so that they stand in the gaps between the cards. The window
            // on the list begins at the first rule and is as wide as a rule; the rail keeps DD1's place, on the
            // rules' right end. A card stands icon_offset into its cell, as every item of DD1's inventories
            // does, and its tooltip beside it (inventory_item_tooltip_layout).
            var items = Dd1Ui.Layout(ItemLayout);
            _itemOffset = Dd1Ui.Offset(items, "inventory_item_layout", "icon_offset", 4f, 0f);
            _itemTip = Dd1Ui.Offset(items, "inventory_item_tooltip_layout", "offset", 85f, 0f);
            _itemTipWidth = Dd1Ui.Number(items, "inventory_item_tooltip_layout", "text_width", 200f);
            _gridAt = Dd1Ui.Offset(layout, main, "inventory_grid_pos", 30f, 195f);
            var gridSize = Dd1Ui.Offset(layout, main, "inventory_grid_size", 560f, 525f);
            _rulesLift = Dd1Ui.Number(layout, main, "grid_visuals_offset", -15f);
            _columns = Mathf.Max(1, Mathf.RoundToInt(Dd1Ui.Number(layout, grid, "number_of_columns", 7f)));
            _cellStart = Dd1Ui.Offset(layout, grid, "start_pos", 0f, 0f);
            _pitch = Dd1Ui.Offset(layout, grid, "offset", 80f, 160f);
            var rail = Dd1Ui.Offset(layout, main, "scroll_bar_offset", 20f, 0f);
            // the rule is centred in the panel, on a whole pixel (DD1 draws its art on whole pixels)
            var window = new Vector2(Mathf.Floor((PanelSize.x - RowRule.x) * 0.5f), _gridAt.y + _rulesLift);
            var windowSize = new Vector2(RowRule.x, gridSize.y - _rulesLift);
            _rowsTop = -_rulesLift;
            _gridAt.x -= window.x;      // from here on: the grid's corner inside the list
            // The bar comes and goes with a fourth row of trinkets (scroll_max_visible_rows), and its arrows are
            // this screen's own art.
            _list = Dd1Ui.ScrollList("Grid", content, window, windowSize, _gridAt.x + gridSize.x + rail.x - windowSize.x, out _scroll, false, false);
            _scroll.scrollSensitivity = _pitch.y * 0.5f;
            _windowHeight = windowSize.y;
            // DD1's scroll bar stands scroll_bar_offset from the grid, which begins grid_visuals_offset below
            // the window's first rule: the rail is as tall as the grid, with DD1's caps past its two ends.
            // MOD: DD1's file has no place for the two arrows; they stand past the caps, on the rail's middle line.
            var railTop = window.y - _rulesLift;
            var bar = Dd1ScrollBar.Of(_scroll);
            bar.Rail.anchoredPosition = new Vector2(bar.Rail.anchoredPosition.x, -railTop);
            bar.Rail.sizeDelta = new Vector2(bar.Rail.sizeDelta.x, gridSize.y);
            bar.Step = _pitch.y;
            var arrowLeft = Mathf.Round(window.x + _gridAt.x + gridSize.x + rail.x + (RailWidth - ArrowSize.x) * 0.5f);
            bar.Follow(
                bar.Arrow("ScrollUp", content, Dir + "realm_inventory_uparrow.png", new Vector2(arrowLeft, railTop - CapArt - ArrowSize.y + 6f), -1, "^"),
                bar.Arrow("ScrollDown", content, Dir + "realm_inventory_downarrow.png", new Vector2(arrowLeft, railTop + gridSize.y + CapArt - 6f), 1, "v"));

            // Tooltips are placed in the screen's own pixels: the panel's own things in the room left of the
            // roster. A card's tooltip stands beside the card as DD1's does (inventory_item_tooltip_layout),
            // which for the grid's last columns is over the roster: it is drawn on the hamlet's canvas, over
            // everything, and goes with the panel.
            _tips = _panel.AddPart("Tips", Vector2.zero, new Vector2(TownPanel.RoomWidth, _panel.BarTop), TownPanel.Side.Right);
            _tooltip = new Dd1Tooltip(_tips, TownPanel.RoomWidth, _panel.BarTop);
            var canvas = (RectTransform)RosterPanel.CanvasRoot;
            _cardTips = UiKit.Rect("TrinketTips", canvas);
            _cardTips.anchorMin = Vector2.zero;
            _cardTips.anchorMax = Vector2.one;
            _cardTips.pivot = Dd1Ui.TopLeft;
            _cardTips.offsetMin = Vector2.zero;
            _cardTips.offsetMax = Vector2.zero;
            _cardTip = new Dd1Tooltip(_cardTips, canvas.rect.width, canvas.rect.height);

            // the hero of the sheet that is up; with none up the panel stands alone, as DD1's does
            _sheetHero = SheetEquipment.Hero;
            _guid = _sheetHero;
        }

        private void Sort(Transform parent, Vector2 at, Vector2 tipAt, RealmInventory.Order order, string art, string dd1String, string dd1Words)
        {
            var button = Dd1Ui.ArtButton("Sort." + order, parent, Dir + art, at, SortSize, () => SortBy(order), order.ToString().Substring(0, 1));
            var rect = (RectTransform)button.transform;
            _sortButtons[order] = rect;
            UiKit.Hover(button.gameObject, inside => Tip(button, inside, rect, tipAt, null, WindowText.Plain(dd1String) ?? dd1Words, true));
        }

        // ---- the player's hand ---------------------------------------------------------------------------

        // First click: this order; second: reversed; third: back to the stores' own (DD1's "Custom").
        private void SortBy(RealmInventory.Order order)
        {
            if (_order != order)
            {
                _order = order;
                _descending = false;
            }
            else if (!_descending) _descending = true;
            else _order = RealmInventory.Order.Arrival;
            // DD1 lets the cards travel to their new places (sort_time) and has a sound for it
            _sortedFrom = new Dictionary<string, Vector2>(_places);
            _sortedAt = Time.unscaledTime;
            Dd2.EstateAudio.Ui("ui/town/sort_by");
            Redraw();
        }

        // A hero handed over from the roster: the hero at hand from now on, his sheet beside the panel.
        private void PickHero(uint guid)
        {
            if (UpgradeUi.Hero(guid) == null) return;
            _guid = guid;
            _pickedAt = Time.unscaledTime;
            Say(null);
            Redraw();
            if (SheetEquipment.Hero == guid) return;
            try { UpgradeUi.ShowSheet(guid); }
            catch (Exception e) { Plugin.Log.LogWarning("Trinket inventory: the sheet of #" + guid + " did not open: " + e.Message); }
        }

        // The sheet's hero is the hero at hand: a sheet that opens, steps on to another hero (its own arrows) or
        // closes says who that is. A hero just handed over from the roster is waited for: his sheet is on its way
        // (and if it never comes, the hero at hand is again whoever's sheet is up, or nobody).
        private void FollowSheet()
        {
            var hero = SheetEquipment.Hero;
            if (hero == _guid)
            {
                _sheetHero = hero;
                return;
            }
            if (hero == _sheetHero && Time.unscaledTime < _pickedAt + SheetComes) return;
            _sheetHero = hero;
            _guid = hero;
            Say(null);
            Redraw();
        }

        // DD1 asks first ("Unequip all trinkets on heroes?"), in its own dialog, and says nothing afterwards.
        private void UnequipAll()
        {
            TownConfirm.Ask(WindowText.Plain("str_unequip_all_trinkets_description") ?? "Unequip all trinkets on heroes?",
                WindowText.Plain("str_unequip_all_trinkets_yes") ?? "Yes", WindowText.Plain("str_unequip_all_trinkets_no") ?? "No",
                () =>
                {
                    RealmInventory.UnequipAll();
                    if (this != null) Redraw();
                });
        }

        // A click on a card of the stores: sold with Shift held (DD1), else put on the hero.
        private void ClickStored(string id)
        {
            if (ShiftHeld)
            {
                AskToSell(id);
                return;
            }
            if (_guid == 0u)
            {
                Say(NoHero, true);
                return;
            }
            Report(RealmInventory.Equip(_guid, id));
        }

        private void ClickWorn(string id)
        {
            if (_guid != 0u) Report(RealmInventory.Unequip(_guid, id));
        }

        // Where a card of the stores was let go: a hero's roster row, one of the sheet's two slots, the hero's
        // sheet anywhere else (DD1: a trinket is dragged onto the hero).
        private void DropStored(string id, PointerEventData pointer)
        {
            var row = RowUnder(pointer);
            if (row != 0u)
            {
                PickHero(row);
                Report(RealmInventory.Equip(row, id));
                return;
            }
            var hero = SheetEquipment.Hero;
            if (hero == 0u || !SheetEquipment.Over(pointer.position)) return;
            // a slot with a trinket in it is still a place to drop on: the two change places
            var slot = SheetEquipment.SlotAt(pointer.position);
            Report(slot >= 0 ? RealmInventory.Equip(hero, id, slot) : RealmInventory.Equip(hero, id));
        }

        private void Report(string trouble)
        {
            if (trouble != null) Say(trouble + ".", true);
            else Say(null);
            Redraw();
        }

        private const string NoHero = "Click a hero in the roster first: the sheet opens beside the trinkets.";

        /// <summary>
        /// A worn trinket's card on the hero's sheet was clicked or let go somewhere (<see cref="SheetEquipment"/>):
        /// on another hero's roster row it changes hands, on the other slot of the sheet it changes places with
        /// what lies there (also with nothing: DD1 lets a trinket be carried from one slot to the other), on
        /// the open panel (and by a click while the panel is open) it goes back to the stores. Returns true
        /// when this was a move of the inventory's.
        /// </summary>
        public static bool WornMoved(uint guid, string id, PointerEventData dropped)
        {
            string trouble;
            if (dropped == null)
            {
                if (!IsOpen) return false;
                trouble = RealmInventory.Unequip(guid, id);
            }
            else
            {
                var row = RowUnder(dropped);
                var slot = row == 0u && SheetEquipment.Hero == guid ? SheetEquipment.SlotAt(dropped.position) : -1;
                if (row != 0u && row != guid) trouble = RealmInventory.Give(guid, id, row);
                else if (slot >= 0)
                {
                    // let go on the slot it was picked up from: it has not gone anywhere
                    if (RealmInventory.Worn(guid).IndexOf(id) == slot) return false;
                    trouble = RealmInventory.MoveWorn(guid, id, slot);
                }
                else if (IsOpen && Over(_instance._inventory, dropped)) trouble = RealmInventory.Unequip(guid, id);
                else return false;
            }
            if (IsOpen) _instance.Report(trouble);
            // nothing closes a panel behind a move made without one: saved at once
            // (and nothing says why a move was refused there: DD2's "invalid" click is all, as on DD2's own sheet)
            else RealmInventory.Flush();
            return true;
        }

        private static bool Over(RectTransform rect, PointerEventData pointer)
        {
            return rect != null && rect.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rect, pointer.position, pointer.pressEventCamera);
        }

        private static bool ShiftHeld
        {
            get
            {
                var keyboard = Keyboard.current;
                return keyboard != null && keyboard.shiftKey.isPressed;
            }
        }

        // DD1 asks before a trinket is sold ("Really sell %s for %d gold?"), in its own dialog, with three
        // answers: its string table has "Yes", "No" and "Always" (realm_inventory_sell_trinket_confirm_*).
        // GUESS: their order on the dialog, and that "Always" sells this one and stops the asking until the
        // game is closed; the files have the word only.
        private void AskToSell(string id)
        {
            var price = Trinkets.SellPrice(id);
            if (price <= 0)
            {
                Say("The wagon does not buy trophies.", true);
                return;
            }
            if (_sellUnasked)
            {
                Sell(id, price);
                return;
            }
            var question = WindowText.Format("realm_inventory_sell_trinket_confirm_question_format", "Really sell %s for %d gold?", Trinkets.Name(id), price);
            var answers = new[]
            {
                WindowText.Plain("realm_inventory_sell_trinket_confirm_yes") ?? "Yes", WindowText.Plain("realm_inventory_sell_trinket_confirm_no") ?? "No",
                WindowText.Plain("realm_inventory_sell_trinket_confirm_always") ?? "Always"
            };
            TownConfirm.Ask(null, question, answers, answer =>
            {
                if (answer == 1) return;
                if (answer == 2) _sellUnasked = true;
                Sell(id, price);
            });
        }

        private void Sell(string id, int price)
        {
            var name = Trinkets.Name(id);
            var trouble = RealmInventory.Sell(id);
            // DD1's coins
            if (trouble == null) Dd2.EstateAudio.Ui("ui/town/sell");
            if (this == null) return;
            Say(trouble != null ? trouble + "." : name + " sold for " + price + " gold.", trouble != null);
            Redraw();
        }

        // ---- what the panel says -------------------------------------------------------------------------

        private void Say(string text, bool trouble = false)
        {
            _said = text;
            _saidTrouble = trouble;
            _saidUntil = text == null ? 0f : Time.unscaledTime + SayLasts;
        }

        /// <summary>
        /// A tooltip beside one of the panel's own things: its upper left corner <paramref name="offset"/> from
        /// the thing's corner, or, <paramref name="above"/>, the middle of its lower edge there (DD1's small buttons:
        /// the box stands centred over the button, not on it; GUESS for this panel, read off the roster's sort buttons).
        /// </summary>
        private void Tip(object owner, bool show, RectTransform of, Vector2 offset, string title, string body, bool above = false)
        {
            if (_tooltip == null) return;
            if (!show) _tooltip.Hide(owner);
            else if (above) _tooltip.ShowOver(owner, OnScreen(of) + offset, title, body);
            else _tooltip.ShowAt(owner, OnScreen(of) + offset, title, body);
        }

        // A corner in the pixels the tooltip is placed in, wherever the thing has scrolled to.
        private Vector2 OnScreen(RectTransform rect) => CornerIn(_tips, rect);

        private static Vector2 CornerIn(RectTransform space, RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);          // 1 = the upper left corner
            var local = space.InverseTransformPoint(corners[1]);
            return new Vector2(local.x, -local.y);
        }

        // DD1's tooltip of an item: beside its card, level with the card's top (inventory_item_tooltip_layout).
        private void CardTip(Image card, string title, string body)
        {
            if (_cardTips == null) return;
            _cardTips.SetAsLastSibling();
            _cardTip.ShowAt(card, CornerIn(_cardTips, (RectTransform)card.transform) + _itemTip, title, body, _itemTipWidth);
        }

        // Under the title DD1 says how a trinket is sold; with Shift held over a card, what it sells for.
        private void ShowDescription()
        {
            var selling = _said == null && ShiftHeld && _hovered != null ? Trinkets.SellPrice(_hovered) : 0;
            if (_sellIcon.gameObject.activeSelf != selling > 0)
            {
                _sellIcon.gameObject.SetActive(selling > 0);
                _sellAmount.gameObject.SetActive(selling > 0);
            }
            string text;
            Color colour;
            if (_said != null)
            {
                text = _said;
                colour = _saidTrouble ? UiKit.Harmful : UiKit.Neutral;
            }
            else
            {
                // base.colours.darkest: the instruction is "neutral", "Sell trinket for:" is "notable"
                text = selling > 0 ? WindowText.Plain("realm_inventory_trinket_sell_description") ?? "Sell trinket for:"
                    : WindowText.Plain("realm_inventory_trinket_sell_instruction") ?? "Hold [SHIFT] to Sell Trinkets";
                colour = selling > 0 ? Dd1Fonts.Colour("realm_inventory_trinket_sell_description", UiKit.Notable)
                    : Dd1Fonts.Colour("realm_inventory_trinket_sell_instruction", UiKit.Neutral);
            }
            if (_description.text != text) _description.text = text;
            _description.color = colour;
            if (selling > 0) _sellAmount.text = selling.ToString();
        }

        // ---- heroes from the roster ----------------------------------------------------------------------

        // While the inventory is open the roster's rows hand their hero to it, as they do to a building's
        // window (RosterWindow): the row's own click (in or out of the party) is switched off and a drag handle
        // put beside it. Rows are rebuilt when the roster changes, so this is looked after every frame.
        private void HookRoster(bool take)
        {
            var list = RosterPanel.CanvasRoot != null ? RosterPanel.CanvasRoot.Find("Roster/List") : null;
            if (list == null) return;
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
                        handle.Portrait = () =>
                        {
                            var actor = UpgradeUi.Hero(guid);
                            return actor != null ? HeroNames.Portrait(actor) : null;
                        };
                        handle.Clicked = () => PickHero(guid);
                        handle.RightClicked = () => UpgradeUi.ShowSheet(guid);
                        handle.Dropped = pointer =>
                        {
                            if (Over(_inventory, pointer) || SheetEquipment.Over(pointer.position)) PickHero(guid);
                        };
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

        // The hero whose roster row lies under the pointer; 0 for none.
        private static uint RowUnder(PointerEventData pointer)
        {
            var list = RosterPanel.CanvasRoot != null ? RosterPanel.CanvasRoot.Find("Roster/List") : null;
            if (list == null) return 0u;
            for (var i = 0; i < list.childCount; i++)
            {
                var row = list.GetChild(i);
                var click = row.GetComponent<RosterRowClick>();
                if (click != null && Over((RectTransform)row, pointer)) return click.Guid;
            }
            return 0u;
        }

        // ---- state ---------------------------------------------------------------------------------------

        private void Redraw()
        {
            _shown = null;
            Refresh();
        }

        private void Refresh()
        {
            if (_inventory == null || _closed) return;
            FollowSheet();
            if (_guid != 0u && UpgradeUi.Hero(_guid) == null) _guid = 0u;
            LoadIcons();

            var stored = RealmInventory.Stored(_order, _descending);
            // what the hero wears is on the sheet; here it decides which cards are dimmed
            var worn = _guid != 0u ? RealmInventory.Worn(_guid) : new List<string>();
            // Everything the cards show, so they are only rebuilt when something changed.
            var key = new StringBuilder().Append(_guid).Append('|').Append(_order).Append(_descending ? 'v' : '^');
            foreach (var id in worn) key.Append('|').Append(id ?? "-");
            key.Append("||");
            foreach (var id in stored) key.Append('|').Append(id);
            if (key.ToString() == _shown) return;
            _shown = key.ToString();

            _tooltip.Hide(null);
            _cardTip.Hide(null);
            _hovered = null;
            UpgradeUi.Clear(_list);
            _places.Clear();
            _travelling.Clear();

            var filled = (stored.Count + _columns - 1) / _columns;
            var rows = Mathf.Max(RowsShown, filled);
            for (var row = 0; row <= rows; row++)
            {
                var top = _rowsTop + row * _pitch.y + _rulesLift;
                Dd1Ui.Art("RowRule" + row, _list, Dir + "realminventory_h_grid.png", new Vector2(0f, top), RowRule);
                // the first of the six lines is 2 pixels into the art; it stands in the middle of the gap after the first card
                if (row < rows) Dd1Ui.Art("ColumnRules" + row, _list, Dir + "realminventory_v_grid.png", new Vector2(_gridAt.x + _cellStart.x + _itemOffset.x + Card.x + (_pitch.x - Card.x) * 0.5f - 2f, top), ColumnRules);
            }
            for (var i = 0; i < stored.Count; i++)
                AddStored(stored[i], new Vector2(_gridAt.x + _cellStart.x + _pitch.x * (i % _columns), _rowsTop + _cellStart.y + _pitch.y * (i / _columns)) + _itemOffset);
            _sortedFrom = null;
            // DD1's window shows three rows and the head of a fourth: the list only grows past the window, and
            // can only be scrolled, once a fourth row has a trinket in it.
            var height = Mathf.Max(_windowHeight, _rowsTop + filled * _pitch.y);
            _list.sizeDelta = new Vector2(0f, height);

            var marked = _sortButtons.TryGetValue(_order, out var button);
            _sortMark.gameObject.SetActive(marked);
            if (marked)
            {
                var offset = _descending ? _markDown : _markUp;
                _sortMark.anchoredPosition = button.anchoredPosition + new Vector2(offset.x, -offset.y);
            }
            LoadIcons();
        }

        private void AddStored(string id, Vector2 at)
        {
            var grade = Trinkets.Grade(id);
            // dimmed: a trinket the hero could wear in no slot. Full slots dim nothing: a drop on one changes places.
            var refusal = _guid != 0u ? RealmInventory.WearRefusal(_guid, id) : null;
            var full = _guid != 0u && refusal == null && RealmInventory.EquipRefusal(_guid, id) != null;
            var card = AddCard(_list, "Stored." + id, id, grade, at, refusal != null);
            _places[id] = at;
            if (_sortedFrom != null && _sortTime > 0f && _sortedFrom.TryGetValue(id, out var was) && was != at)
            {
                var rect = (RectTransform)card.transform;
                rect.anchoredPosition = new Vector2(was.x, -was.y);
                _travelling.Add(new KeyValuePair<RectTransform, Vector2[]>(rect, new[] { was, at }));
            }
            var drag = card.gameObject.AddComponent<TrinketDrag>();
            drag.Card = () => card.sprite;
            drag.Icon = () => Trinkets.Icon(id);
            drag.Clicked = () => ClickStored(id);
            drag.RightClicked = TownPanel.Close;
            drag.Dropped = pointer => DropStored(id, pointer);
            drag.Hovered = inside =>
            {
                _hovered = inside ? id : _hovered == id ? null : _hovered;
                if (!inside)
                {
                    _cardTip.Hide(card);
                    return;
                }
                var price = Trinkets.SellPrice(id);
                var action = _guid == 0u ? "Click a hero in the roster to put it on, or drag it onto a hero's row."
                    : refusal != null ? Dd1Ui.Tint(refusal + ".", "harmful")
                    : full ? "Both slots are taken: drag it onto one to change places."
                    : "Click to put it on " + UpgradeUi.Hero(_guid)?.ActorName + ", or drag it onto the hero.";
                action += "\n" + (price > 0 ? (WindowText.Plain("realm_inventory_trinket_sell_description") ?? "Sell trinket for:") + " " + price + " gold (Shift and click)." : "The wagon does not buy trophies.");
                CardTip(card, Trinkets.Name(id), Body(id, grade, action));
            };
        }

        /// <summary>
        /// The card of a trinket a hero wears, for the Equipment block of the hero's sheet (<see cref="SheetEquipment"/>):
        /// the stores' own card, made by the same code, at <paramref name="at"/> in the parent's pixels. Whoever shows
        /// it keeps <see cref="LoadIcons"/> going until DD2 has handed its picture over.
        /// </summary>
        internal static TrinketDrag WornCard(Transform parent, string id, Vector2 at)
        {
            var card = AddCard(parent, "Worn." + id, id, Trinkets.Grade(id), at, false);
            var drag = card.gameObject.AddComponent<TrinketDrag>();
            drag.Card = () => card.sprite;
            drag.Icon = () => Trinkets.Icon(id);
            return drag;
        }

        /// <summary>
        /// What a trinket's tooltip says under its name (see <see cref="Body"/>), for the same card elsewhere;
        /// without an <paramref name="action"/> (a card that is only looked at) the last line is left out.
        /// </summary>
        internal static string CardText(string id, string action)
        {
            var text = Body(id, Trinkets.Grade(id), action ?? "");
            return string.IsNullOrEmpty(action) ? text.Substring(0, text.LastIndexOf('\n')) : text;
        }

        // DD1's card of the rarity with DD2's picture of the trinket (TrinketPicture). As in DD1 a card is brighter
        // under the pointer (base.colours.darkest inventory_selected), and an ancestral trinket and a trophy
        // have their sparkle running round the card (fx/trinket_sparkle: its skeleton's origin is the card's middle).
        private static Image AddCard(Transform parent, string name, string id, string rarity, Vector2 at, bool faded)
        {
            var card = Dd1Ui.Art(name, parent, Trinkets.RarityArt(rarity), at, Card, new Color(0.1f, 0.09f, 0.08f, 0.95f), true);
            // inventory_unselectable is #666 (and takes most of the colour out, which a UI picture cannot do)
            if (faded) card.color = new Color(card.color.r * 0.4f, card.color.g * 0.4f, card.color.b * 0.4f, card.color.a);
            var icon = TrinketPicture.Add(card.transform, faded ? "IconFaded" : "Icon");
            _iconsWanted.Add(new KeyValuePair<Image, string>(icon, id));
            var light = card.gameObject.AddComponent<Dd1Highlight>();
            light.Factor = "inventory_selected";
            light.Stock = new Color(1.4f, 1.4f, 1.7f, 1f);       // FALLBACK: DD1's stock inventory_selected
            light.WithChildren = true;
            var sparkle = rarity == "ancestral" ? "ancestral_sparkle" : rarity == Trinkets.Trophy ? "boss_sparkle" : null;
            var around = sparkle != null ? SpineView.Loop("Sparkle", card.transform, Sparkle, Card * 0.5f, sparkle) : null;
            // a store can hold dozens of them: drawn as often as DD1 keys its animations
            if (around != null) around.Step = 1f / 30f;
            return card;
        }

        // Under the trinket's name: its DD1 rarity, what DD2 says the item does, what a click would do.
        private static string Body(string id, string rarity, string action)
        {
            var text = Dd1Ui.Tint(QuestMapText.TrinketRarity(rarity), "notable");
            var description = Trinkets.Description(id);
            if (description.Length > 0) text += "\n" + description;
            return text + "\n" + Dd1Ui.Tint(action, "town_building_info");
        }

        // DD2 loads item icons on demand: the cards ask until theirs has come.
        internal static void LoadIcons()
        {
            for (var i = _iconsWanted.Count - 1; i >= 0; i--)
            {
                var image = _iconsWanted[i].Key;
                if (image == null)
                {
                    _iconsWanted.RemoveAt(i);
                    continue;
                }
                var sprite = Trinkets.Icon(_iconsWanted[i].Value);
                if (sprite == null)
                {
                    if (Trinkets.HasNoIcon(_iconsWanted[i].Value)) _iconsWanted.RemoveAt(i);
                    continue;
                }
                image.sprite = sprite;
                image.color = image.gameObject.name == "IconFaded" ? new Color(0.4f, 0.4f, 0.4f, 1f) : Color.white;
                _iconsWanted.RemoveAt(i);
            }
        }

        private void Update()
        {
            // closed this frame: the roster's rows are the party's again and stay so
            if (_inventory == null || _closed) return;
            HookRoster(true);
            FollowSheet();
            if (_said != null && Time.unscaledTime > _saidUntil) Say(null);
            ShowDescription();
            Travel();
            // icons come a few frames after they are asked for; the panel's own refresh is only twice a second
            if (_iconsWanted.Count == 0 || Time.unscaledTime < _nextIcons) return;
            _nextIcons = Time.unscaledTime + 0.1f;
            LoadIcons();
        }

        // After a sort the cards go to their new places in sort_time (DD1's file has no easing for it).
        private void Travel()
        {
            if (_travelling.Count == 0) return;
            var share = Mathf.Clamp01((Time.unscaledTime - _sortedAt) / _sortTime);
            foreach (var card in _travelling)
            {
                if (card.Key == null) continue;
                var at = Vector2.Lerp(card.Value[0], card.Value[1], share);
                card.Key.anchoredPosition = new Vector2(at.x, -at.y);
            }
            if (share >= 1f) _travelling.Clear();
        }

        // ---- dev bridge ----------------------------------------------------------------------------------

        public static object Snapshot()
        {
            if (!IsOpen) return new { open = false };
            var view = _instance;
            return new
            {
                open = true, hero = view._guid, order = _order.ToString().ToLowerInvariant(), descending = _descending, said = view._said,
                cards = view._list.childCount, iconsLoading = _iconsWanted.Count, listHeight = view._list.rect.height,
                at = view._scroll.verticalNormalizedPosition, box = SheetEquipment.Box(view._inventory), sheetHero = SheetEquipment.Hero
            };
        }

        /// <summary>The panel's box on screen (the mod's pixels: 1080 high, y down); nothing while it is closed.</summary>
        public static Rect? ScreenBox => IsOpen && _instance._inventory != null ? SheetEquipment.ScreenBox(_instance._inventory) : (Rect?)null;

        /// <summary>For tests: where a card of the stores is on screen (real pixels from the bottom left); null for none.</summary>
        public static Vector2? CardAt(string id)
        {
            var card = IsOpen ? _instance._list.Find("Stored." + id) as RectTransform : null;
            if (card == null) return null;
            var corners = new Vector3[4];
            card.GetWorldCorners(corners);
            return (corners[0] + corners[2]) * 0.5f;
        }

        /// <summary>For tests: what a click on a hero's roster row does while the inventory is open.</summary>
        public static bool Pick(uint guid)
        {
            if (!IsOpen || UpgradeUi.Hero(guid) == null) return false;
            _instance.PickHero(guid);
            return true;
        }

        /// <summary>For tests: what the sort buttons do.</summary>
        public static void SetOrder(RealmInventory.Order order, bool descending)
        {
            _order = order;
            _descending = descending;
            if (IsOpen) _instance.Redraw();
        }

        /// <summary>For tests: what a click on a card does (stored: on; worn: off).</summary>
        public static string Click(string id)
        {
            if (!IsOpen) return "not open";
            var view = _instance;
            if (view._guid != 0u && RealmInventory.Worn(view._guid).Contains(id)) view.ClickWorn(id);
            else view.ClickStored(id);
            return view._said;
        }
    }

    /// <summary>
    /// A trinket's card that can be clicked or picked up: while it is dragged a copy of the card follows the
    /// pointer; where it is let go is for the owner to judge. A plain click is told apart from a drag.
    /// </summary>
    internal class TrinketDrag : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private static readonly Vector2 Size = new Vector2(72f, 144f);

        public Func<Sprite> Card, Icon;
        public Action Clicked, RightClicked;
        public Action<bool> Hovered;
        public Action<PointerEventData> Dropped;
        /// <summary>The card cannot be picked up (a trinket DD2 does not let go of).</summary>
        public bool Fixed;

        private RectTransform _ghost;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.dragging) return;
            if (eventData.button == PointerEventData.InputButton.Left) Clicked?.Invoke();
            else if (eventData.button == PointerEventData.InputButton.Right) RightClicked?.Invoke();
        }

        public void OnPointerEnter(PointerEventData eventData) => Hovered?.Invoke(true);

        public void OnPointerExit(PointerEventData eventData) => Hovered?.Invoke(false);

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (Fixed || eventData.button != PointerEventData.InputButton.Left || RosterPanel.CanvasRoot == null) return;
            Hovered?.Invoke(false);
            var card = Card?.Invoke();
            var image = UiKit.Image("DraggedTrinket", SheetEquipment.Overlay ?? RosterPanel.CanvasRoot, card, card != null ? new Color(1f, 1f, 1f, 0.92f) : new Color(0.12f, 0.1f, 0.08f, 0.92f));
            _ghost = ((RectTransform)image.transform).Place(Dd1Ui.Middle, Dd1Ui.Middle, Vector2.zero, Size);
            var icon = Icon?.Invoke();
            if (icon != null)
            {
                var top = TrinketPicture.Add(_ghost);
                top.sprite = icon;
                top.color = Color.white;
            }
            Follow(eventData);
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
            if (_ghost != null) Destroy(_ghost.gameObject);
            _ghost = null;
        }

        private void OnDisable()
        {
            Drop();
            Hovered?.Invoke(false);
        }
    }
}
