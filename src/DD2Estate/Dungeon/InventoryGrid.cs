using System;
using System.Collections.Generic;
using DD2Estate.Core;
using DD2Estate.Dd1;
using DD2Estate.Estate;
using DD2Estate.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// A grid of DD1 item cards the way DD1 lays an inventory out (shared/inventory/inventory.layout.darkest,
    /// inventory_item_layout): a cell per slot, the 72x144 card inside it at icon_offset, the stack's size at
    /// amount_text_offset in DD1's inventory_amount style. The cells themselves are drawn by the panel picture
    /// under the grid (the raid inventory, the provision screen's two grids, the loot scroll), so a slot has no
    /// ground of its own. The card under the pointer wears overlays/eqp_mouseover.png, as in DD1.
    ///
    /// A trinket's card (a trinket found on the way lies in the bag and on the loot scroll like anything else)
    /// is DD1's card of its rarity with DD2's own picture of the trinket on it, as the Trinket Inventory draws
    /// it (<see cref="TrinketPicture"/>), and has no number on it. DD2 loads such a picture on demand: the card
    /// asks until it has come.
    ///
    /// Used for the party's bag in the dungeon HUD, for both grids of the provision screen and for the loot
    /// scroll. The owner fills the slots with <see cref="Set"/> and gets clicks and hovers back by slot index.
    /// </summary>
    internal class InventoryGrid : MonoBehaviour
    {
        public static readonly Vector2 Card = new Vector2(72f, 144f);
        private static readonly Vector2 HoverArt = new Vector2(118f, 187f);     // overlays/eqp_mouseover.png
        private const float IconsEvery = 0.1f;          // seconds between asks for a picture DD2 is still loading

        private static readonly Vector2 MarkArt = new Vector2(32f, 32f);        // panels/icons_curio_tracker/*.curio_tracker.png

        private class SlotView
        {
            public RectTransform Cell;
            public Image Icon, Hover, Mark, Trinket;
            public TextMeshProUGUI Count, Shadow;
            public ItemDef Item;
            public int Amount = -1;
            public bool Dim, Inside;
            public string MarkArt;
            /// <summary>The trinket whose picture the card still waits for; null when it shows or there is none.</summary>
            public string Wanted;
        }

        private readonly List<SlotView> _slots = new List<SlotView>();
        private int _selected = -1;
        private int _carried = -1;
        private float _nextIcons;

        /// <summary>Slot index, and whether it was the right button.</summary>
        public Action<int, bool> Clicked;
        /// <summary>Slot index the pointer went onto, or -1 when it left one.</summary>
        public Action<int> Hovered;

        /// <summary>
        /// DD1's inventory lets a card be picked up with the left button and carried (to another cell, to a
        /// curio's slot, to a hero). Off for the grids that only take clicks (the provision screen's, the loot scroll's).
        /// </summary>
        public bool Draggable;
        /// <summary>A card is picked up: its slot. Then where the pointer carries it (screen pixels), and where it is let go.</summary>
        public Action<int> DragBegan;
        public Action<int, Vector2> DragMoved, DragEnded;

        public int SlotCount => _slots.Count;

        /// <summary>The slot whose card is in the player's hand (it is drawn dark in its cell meanwhile); -1 for none.</summary>
        public int Carried
        {
            get => _carried;
            set
            {
                if (_carried == value) return;
                var old = _carried;
                _carried = value;
                if (old >= 0 && old < _slots.Count) Paint(_slots[old], old);
                if (_carried >= 0 && _carried < _slots.Count) Paint(_slots[_carried], _carried);
            }
        }

        /// <summary>The slot under a point of the screen; -1 for none.</summary>
        public int SlotAt(Vector2 screen)
        {
            for (var i = 0; i < _slots.Count; i++)
                if (RectTransformUtility.RectangleContainsScreenPoint(_slots[i].Cell, screen, null)) return i;
            return -1;
        }

        /// <param name="topLeft">The first cell's corner (a grid layout's start_pos).</param>
        /// <param name="pitch">Distance between cell corners (a grid layout's offset).</param>
        public static InventoryGrid Build(string name, Transform parent, Vector2 topLeft, int slots, int columns, Vector2 pitch)
        {
            var layout = RaidLayout.Current;
            var rows = (slots + columns - 1) / columns;
            var root = UiKit.Rect(name, parent);
            root.PlaceTopLeft(topLeft, RaidUi.TopLeft, new Vector2(pitch.x * columns, pitch.y * rows));
            var grid = root.gameObject.AddComponent<InventoryGrid>();
            for (var i = 0; i < slots; i++)
            {
                var index = i;
                var view = new SlotView();
                // the card's own rectangle takes the pointer: the gap DD1 leaves between two cells belongs to neither
                var cell = UiKit.Image("Slot" + i, root, null, Color.clear, true);
                view.Cell = (RectTransform)cell.transform;
                view.Cell.PlaceTopLeft(new Vector2(pitch.x * (i % columns), pitch.y * (i / columns)) + layout.ItemIconOffset, RaidUi.TopLeft, layout.ItemIconSize);
                var input = cell.gameObject.AddComponent<SlotInput>();
                input.Click = right => grid.Clicked?.Invoke(index, right);
                input.Hover = inside =>
                {
                    view.Inside = inside;
                    grid.Mark(view, index);
                    grid.Hovered?.Invoke(inside ? index : -1);
                };
                input.CanDrag = () => grid.Draggable && view.Item != null;
                input.DragBegan = () => grid.DragBegan?.Invoke(index);
                input.DragMoved = at => grid.DragMoved?.Invoke(index, at);
                input.DragEnded = at => grid.DragEnded?.Invoke(index, at);

                view.Icon = UiKit.Image("Icon", view.Cell, null);
                UiKit.Stretch((RectTransform)view.Icon.transform);
                view.Icon.gameObject.SetActive(false);

                view.Trinket = TrinketPicture.Add(view.Cell, "Trinket");
                view.Trinket.gameObject.SetActive(false);

                view.Hover = UiKit.Image("Hover", view.Cell, RaidUi.Sprite("overlays/eqp_mouseover.png"), new Color(1f, 1f, 1f, 0f));
                ((RectTransform)view.Hover.transform).Place(RaidUi.Middle, RaidUi.Middle, Vector2.zero, HoverArt);
                view.Hover.gameObject.SetActive(false);

                // amount_text_offset is counted from the cell's corner, the card sits icon_offset inside it
                var at = layout.ItemAmount - layout.ItemIconOffset;
                view.Count = RaidUi.Label("Count", view.Cell, "inventory_amount", at, new Vector2(layout.ItemIconSize.x - at.x, 40f));
                // some cards are bright in the corner (coins, a crest): the count gets a black twin behind it
                view.Shadow = RaidUi.Shadow(view.Count);

                // DD1's curio tracker mark under the card's picture (inventory_curio_tracker_layout.icon_offset,
                // from the cell's corner)
                view.Mark = UiKit.Image("Mark", view.Cell, null);
                ((RectTransform)view.Mark.transform).PlaceTopLeft(layout.ItemTracker - layout.ItemIconOffset, RaidUi.TopLeft, MarkArt);
                view.Mark.gameObject.SetActive(false);
                grid._slots.Add(view);
            }
            return grid;
        }

        /// <summary>
        /// A mark on a slot's card: DD1's picture of what the item is known to do to the curio the party has
        /// turned to (panels/icons_curio_tracker); null for none.
        /// </summary>
        public void SetMark(int index, string dd1Art)
        {
            if (index < 0 || index >= _slots.Count) return;
            var view = _slots[index];
            if (view.MarkArt == dd1Art) return;
            view.MarkArt = dd1Art;
            var sprite = dd1Art != null ? RaidUi.Sprite(dd1Art) : null;
            view.Mark.sprite = sprite;
            view.Mark.gameObject.SetActive(sprite != null);
        }

        /// <summary>Top left corner of a slot's cell inside the grid, y down.</summary>
        public Vector2 SlotPosition(int index)
        {
            var rt = _slots[index].Cell;
            return new Vector2(rt.anchoredPosition.x, -rt.anchoredPosition.y) - RaidLayout.Current.ItemIconOffset;
        }

        /// <summary>The card of a slot, for a tooltip that hangs beside it.</summary>
        public RectTransform SlotRect(int index) => index >= 0 && index < _slots.Count ? _slots[index].Cell : null;

        /// <summary>Shows a stack in a slot (null item: empty). A dimmed card is one that cannot be had right now.</summary>
        public void Set(int index, ItemDef item, int amount, bool dim = false)
        {
            if (index < 0 || index >= _slots.Count) return;
            var view = _slots[index];
            if (view.Item == item && view.Amount == amount && view.Dim == dim) return;
            view.Item = item;
            view.Amount = amount;
            view.Dim = dim;
            Paint(view, index);
            Mark(view, index);
        }

        // A slot's card and count. DD1 draws what cannot be had or used right now grey and nearly without colour
        // (colours/base.colours.darkest: inventory_unselectable .rgba #666 .saturation 0.2, the count in
        // inventory_amount_unselectable); a card in the player's hand is dark in its cell the same way.
        private void Paint(SlotView view, int index)
        {
            var item = view.Item;
            var dark = view.Dim || index == _carried;
            var sprite = item != null ? InventoryContent.Icon(item, view.Amount) : null;
            if (dark && sprite != null) sprite = RaidUi.Desaturated(sprite, RaidUi.ColourNumber("inventory_unselectable", "saturation", 0.2f)) ?? sprite;
            view.Icon.gameObject.SetActive(item != null);
            view.Icon.sprite = sprite;
            // an item without DD1 art still shows as a box with its count; DD1 greys what cannot be picked (inventory_unselectable)
            view.Icon.color = sprite == null ? new Color(0.25f, 0.2f, 0.12f) : dark ? Dd1Fonts.Colour("inventory_unselectable", new Color(0.4f, 0.4f, 0.4f)) : Color.white;
            view.Count.text = view.Shadow.text = item != null ? InventoryText.Amount(item, view.Amount) : "";
            view.Count.color = dark ? Dd1Fonts.Colour("inventory_amount_unselectable", new Color32(0x5d, 0x5a, 0x50, 255)) : Dd1Fonts.Colour("inventory_amount", UiKit.Notable);
            var trinket = item != null && item.Type == ItemTypes.Trinket;
            view.Trinket.gameObject.SetActive(trinket);
            view.Wanted = trinket ? item.Id : null;
            if (trinket)
            {
                view.Trinket.sprite = null;
                view.Trinket.color = Color.clear;
                ShowTrinket(view);
            }
        }

        // DD2's picture of the trinket, once it has come; a trinket without one keeps the bare card.
        private static void ShowTrinket(SlotView view)
        {
            if (view.Wanted == null) return;
            var sprite = Trinkets.Icon(view.Wanted);
            if (sprite == null)
            {
                if (Trinkets.HasNoIcon(view.Wanted)) view.Wanted = null;
                return;
            }
            view.Trinket.sprite = sprite;
            view.Trinket.color = view.Dim ? Dd1Fonts.Colour("inventory_unselectable", new Color(0.4f, 0.4f, 0.4f)) : Color.white;
            view.Wanted = null;
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextIcons) return;
            _nextIcons = Time.unscaledTime + IconsEvery;
            foreach (var view in _slots)
                if (view.Wanted != null) ShowTrinket(view);
        }

        /// <summary>Shows a bag; <paramref name="dim"/> says which of its stacks cannot be used right now.</summary>
        public void Show(Core.Inventory bag, Func<ItemStack, bool> dim = null)
        {
            for (var i = 0; i < _slots.Count; i++)
            {
                var stack = bag?.Slot(i);
                Set(i, stack?.Item, stack?.Amount ?? 0, stack != null && dim != null && dim(stack));
            }
        }

        /// <summary>The slot that keeps the pointer's frame on; -1 for none.</summary>
        public int Selected
        {
            get => _selected;
            set
            {
                if (_selected == value) return;
                var old = _selected;
                _selected = value;
                if (old >= 0 && old < _slots.Count) Mark(_slots[old], old);
                if (_selected >= 0 && _selected < _slots.Count) Mark(_slots[_selected], _selected);
            }
        }

        private void Mark(SlotView view, int index)
        {
            var on = view.Item != null && (view.Inside || index == _selected);
            if (view.Hover.gameObject.activeSelf != on) view.Hover.gameObject.SetActive(on);
            if (!on) return;
            // the frame is larger than the card: its slot is drawn last so that no neighbour lies over it
            if (view.Inside) view.Cell.SetAsLastSibling();
            var sprite = RaidUi.Sprite(view.Dim ? "overlays/eqp_unavailable_mouseover.png" : "overlays/eqp_mouseover.png");
            view.Hover.sprite = sprite;
            view.Hover.color = sprite != null ? Color.white : new Color(0.86f, 0.71f, 0.36f, 0.25f);
        }

        private class SlotInput : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
        {
            public Action<bool> Click;
            public Action<bool> Hover;
            public Func<bool> CanDrag;
            public Action DragBegan;
            public Action<Vector2> DragMoved, DragEnded;
            private bool _dragging, _dragged;

            public void OnPointerClick(PointerEventData eventData)
            {
                // the release that ends a carry is not a click on the cell it started from
                if (_dragged)
                {
                    _dragged = false;
                    return;
                }
                if (eventData.button == PointerEventData.InputButton.Left) Click?.Invoke(false);
                else if (eventData.button == PointerEventData.InputButton.Right) Click?.Invoke(true);
            }

            public void OnPointerEnter(PointerEventData eventData) => Hover?.Invoke(true);

            public void OnPointerExit(PointerEventData eventData) => Hover?.Invoke(false);

            public void OnBeginDrag(PointerEventData eventData)
            {
                _dragged = false;
                if (eventData.button != PointerEventData.InputButton.Left || CanDrag == null || !CanDrag()) return;
                _dragging = _dragged = true;
                DragBegan?.Invoke();
                DragMoved?.Invoke(eventData.position);
            }

            public void OnDrag(PointerEventData eventData)
            {
                if (_dragging) DragMoved?.Invoke(eventData.position);
            }

            public void OnEndDrag(PointerEventData eventData)
            {
                // the UI sends the click of a release before the end of its drag: by now it has been let pass
                _dragged = false;
                if (!_dragging) return;
                _dragging = false;
                DragEnded?.Invoke(eventData.position);
            }

            private void OnDisable()
            {
                Hover?.Invoke(false);
                // a grid put away with a card in the player's hand: the card goes back
                if (_dragging)
                {
                    _dragging = false;
                    DragEnded?.Invoke(new Vector2(float.MinValue, float.MinValue));
                }
                _dragged = false;
            }
        }
    }
}
