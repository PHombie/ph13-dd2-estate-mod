using System;
using System.Collections.Generic;
using DD2Estate.Core;
using DD2Estate.Dd1;
using DD2Estate.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD2Estate.Dungeon
{
    /// <summary>Which of DD1's windows a question is asked in.</summary>
    internal enum RaidPromptKind
    {
        /// <summary>A plain question: DD1's basic scroll with its answers as lines of text.</summary>
        Plain,
        /// <summary>A curio: the sidebar scroll with the hand, the item slot and the way past.</summary>
        Curio,
        /// <summary>An obstacle: the sidebar scroll with the hand, the slot with the shovel in it and the way past.</summary>
        Obstacle,
        /// <summary>Hunger: the basic scroll with DD1's picture of a meal, Eat and Starve.</summary>
        Hunger
    }

    /// <summary>What an answer is, and so which of DD1's buttons stands for it.</summary>
    internal enum RaidOptionRole { Text, Hand, Pass, Eat, Starve }

    /// <summary>
    /// A question of the expedition, with what DD1's windows need to show it: the kind of window, a heading,
    /// the text, and for every answer its role. On a curio's and an obstacle's scroll there is DD1's slot for an
    /// item of the bag besides: an item is offered to it (dragged onto it, or right-clicked in the inventory,
    /// as DD1 has it: "[RIGHT-CLICK] an inventory item to apply it to the object") and whoever asked says
    /// whether it is taken (<see cref="Offer"/>); the question is then answered with <see cref="ItemAnswer"/>.
    /// </summary>
    internal sealed class RaidPrompt
    {
        /// <summary>The answer of a question whose slot took an item.</summary>
        public const int ItemAnswer = 1000;

        public RaidPromptKind Kind;
        /// <summary>The heading on the scroll's band; null for none.</summary>
        public string Title;
        /// <summary>The scroll's text.</summary>
        public string Body;
        /// <summary>The question as it was asked (the dev bridge reads it back).</summary>
        public string Text;
        public string[] Options = new string[0];
        public RaidOptionRole[] Roles = new RaidOptionRole[0];
        /// <summary>What the pointer is told on an answer's button; null for the answer's own text.</summary>
        public string[] Tooltips = new string[0];
        /// <summary>Hunger with too little food: Eat is shown but cannot be chosen.</summary>
        public bool EatMissing;
        /// <summary>What the pointer is told on the item slot (DD1's own words for it).</summary>
        public string SlotTooltip;
        /// <summary>What lies in the slot from the start: DD1 shows an obstacle's shovel there. Null: the empty slot.</summary>
        public ItemDef SlotItem;
        /// <summary>The slot itself was clicked (an obstacle's: the shovel in it is used).</summary>
        public Action SlotClicked;
        /// <summary>
        /// A stack of the bag (its slot's index) is offered to the scroll's slot. True: it is taken and the
        /// question is answered with <see cref="ItemAnswer"/>; false: it stays in the bag and the scroll stays up.
        /// Null: the scroll takes no item.
        /// </summary>
        public Func<int, bool> Offer;
        /// <summary>
        /// The curio the scroll is about, for DD1's marks of what the player has learnt items do to it (the
        /// inventory's cards wear them while the scroll is up); null for none.
        /// </summary>
        public string TrackerCurio;
        /// <summary>The hand cannot be used: DD1's curio that asks for a quest item (curio_investigate_hand_disabled).</summary>
        public bool HandDisabled;

        public bool TakesItems => Offer != null;

        public int IndexOf(RaidOptionRole role)
        {
            for (var i = 0; i < Roles.Length; i++)
                if (Roles[i] == role) return i;
            return -1;
        }

        public static RaidPrompt Plain(string text, string[] options)
        {
            options = options ?? new string[0];
            return new RaidPrompt
            {
                Kind = RaidPromptKind.Plain, Text = text, Body = text, Options = options,
                Roles = new RaidOptionRole[options.Length], Tooltips = new string[options.Length]
            };
        }

        /// <summary>
        /// A scroll of DD1's with its hand and its way past (in that order; without <paramref name="pass"/> the
        /// hand alone), for a curio or an obstacle.
        /// </summary>
        public static RaidPrompt Sidebar(RaidPromptKind kind, string title, string body, string hand, string handTip, string pass, string passTip)
        {
            var prompt = Plain(body, pass != null ? new[] { hand, pass } : new[] { hand });
            prompt.Kind = kind;
            prompt.Title = title;
            prompt.Roles[0] = RaidOptionRole.Hand;
            prompt.Tooltips[0] = handTip;
            if (pass != null)
            {
                prompt.Roles[1] = RaidOptionRole.Pass;
                prompt.Tooltips[1] = passTip;
            }
            return prompt;
        }

        /// <summary>
        /// The window for a question asked with words alone: DD1's hunger scroll while the party has to eat or
        /// starve (the exploration says so, not the wording), else a plain question. The one about a full bag
        /// gets DD1's heading for it (the only thing read off a question's wording, and only to dress it).
        /// </summary>
        public static RaidPrompt Infer(DungeonRun run, string text, string[] options)
        {
            var prompt = Plain(text, options);
            var x = run?.Exploration;
            if (x == null || prompt.Options.Length == 0) return prompt;
            var n = prompt.Options.Length;
            if (x.Pending == PendingKind.Hunger && n <= 2)
            {
                prompt.Kind = RaidPromptKind.Hunger;
                prompt.Title = RaidText.Get("str_ui_hunger_title", "Hunger");
                prompt.Roles[n - 1] = RaidOptionRole.Starve;
                if (n == 2) prompt.Roles[0] = RaidOptionRole.Eat;
                prompt.EatMissing = n == 1;
            }
            else if (text != null && text.StartsWith("The bag is full", StringComparison.Ordinal)) prompt.Title = RaidText.Get("not_enough_room", "Not enough room!");
            return prompt;
        }

        /// <summary>"thorny_thicket" as "Thorny Thicket": a heading for what DD1 has no name for.</summary>
        public static string Words(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var words = id.Split('_');
            for (var i = 0; i < words.Length; i++)
                if (words[i].Length > 0) words[i] = char.ToUpperInvariant(words[i][0]) + words[i].Substring(1);
            return string.Join(" ", words);
        }
    }

    /// <summary>
    /// DD1's event scrolls, at the places scripts/layout/screen.raid.darkest and overlay.loot.darkest give
    /// them: the sidebar scroll for a curio or an obstacle (heading, text, the hand, the item slot, the way
    /// past), the basic scroll for hunger and for plain questions, the loot scroll with its cards, Take All
    /// and Close. One is open at a time. The scrolls only show and ask; who answers what is the HUD's and the
    /// expedition's business.
    ///
    /// Seen in DD1's own frames (_lab/dd1_ref/raid/raid_curio_window_*.png, raid_obstacle_window_rubble.png,
    /// raid_curio_result_*.png): the hand and the way past stand without words under them (DD1 places those
    /// for a controller only: sidebar_scroll investigate_controller_text_offset, pass_controller_text_offset;
    /// the pointer is told them in tooltips); each of the two is its picture over scrolls/choice_button_frame.png,
    /// the two rails, at choice_button.frame_offset from the layout's position (<see cref="RaidLayout.ChoiceCorner"/>);
    /// the slot's picture stands at item_slot_pos itself; an obstacle's slot holds the shovel; and the loot
    /// scroll hangs where the sidebar scroll does.
    /// </summary>
    internal class RaidScrolls
    {
        private const string Dir = "scrolls/";
        private static readonly Vector2 SidebarSize = new Vector2(456f, 400f);      // event_scroll_sidebar.png
        private static readonly Vector2 BasicSize = new Vector2(456f, 518f);        // event_scroll_basic.png
        private static readonly Vector2 LootSize = new Vector2(456f, 475f);         // event_scroll_loot.png
        private static readonly Vector2 InsetSize = new Vector2(347f, 147f);        // inset_hunger.png
        private static readonly Vector2 ButtonArt = new Vector2(124f, 69f);         // byhand.png, pass.png, eat.png, starve.png
        private static readonly Vector2 LootEdge = new Vector2(16f, 163f);          // event_scroll_loot_left_edge.png / right_edge
        private static readonly Vector2 LootMid = new Vector2(72f, 163f);           // event_scroll_loot_mid_section.png

        // The mod's own numbers (scroll pixels).
        private const float HeaderWidth = 370f;         // the band between the scroll's two gold posts
        private const float HeaderHeight = 64f;         // DwarvenAxe large's line
        private const float TipDrop = 0f;               // a button's tooltip hangs this far under its picture (sidebar_scroll.investigate_tooltip_offset 0 0)
        private const float CaptionRise = 9f;           // a button's caption starts this far above its art's foot (the art ends in a soft glow)
        private const float CaptionWidth = 116f;        // a caption stays between its button's neighbours (the item slot starts 60 px from the hand's middle)
        private const float RowGap = 4f;                // between the answer lines of a plain question
        private static readonly Vector2 PlainBody = new Vector2(0f, 162f);          // a plain question's text takes the picture's frame
        private const float PlainBodyHeight = 240f;

        private readonly RectTransform _screen;
        private readonly RaidTooltip _tooltip;
        private RectTransform _root;
        private InventoryGrid _lootGrid;
        // which stack of the find each card of the loot scroll shows (-1: none)
        private readonly List<int> _lootShown = new List<int>();
        private Image _slot;
        private Image _slotCard;
        private RaidPrompt _prompt;
        private bool _slotLit;

        /// <summary>The answer chosen on a scroll (the index of the option).</summary>
        public Action<int> Answered;

        public bool IsOpen => _root != null;

        /// <summary>The open scroll's slot for an item, as a place to drop one; null when no scroll with a slot is open.</summary>
        public RectTransform ItemSlot => _slot != null && _prompt != null && _prompt.TakesItems ? (RectTransform)_slot.transform : null;

        /// <summary>
        /// The slot's lit picture (scrolls/use_inventory_active.png, the gold edge) while an item of the bag is in
        /// the player's hand. GUESS: DD1's rule for it is not in a file; it is read as "an item may go here now".
        /// </summary>
        public void LightSlot(bool lit)
        {
            if (_slot == null || _slotLit == lit) return;
            _slotLit = lit;
            var sprite = RaidUi.Sprite(Dir + (lit ? "use_inventory_active.png" : "use_inventory.png"));
            if (sprite != null) _slot.sprite = sprite;
        }

        public RaidScrolls(RectTransform screen, RaidTooltip tooltip)
        {
            _screen = screen;
            _tooltip = tooltip;
        }

        public void Close()
        {
            _tooltip.Hide(this);
            if (_root != null)
            {
                _root.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(_root.gameObject);
            }
            _root = null;
            _lootGrid = null;
            _lootShown.Clear();
            _slot = _slotCard = null;
            _slotLit = false;
            _prompt = null;
        }

        public void Show(RaidPrompt prompt)
        {
            Close();
            _prompt = prompt;
            if (prompt.Kind == RaidPromptKind.Hunger || prompt.Kind == RaidPromptKind.Plain) BuildBasic(prompt);
            else BuildSidebar(prompt);
        }

        // ---- the sidebar scroll: curio, obstacle -----------------------------------------------------

        private void BuildSidebar(RaidPrompt prompt)
        {
            var l = RaidLayout.Current;
            var centre = SidebarSize.x * 0.5f;
            _root = Scroll("Scroll.Sidebar", "event_scroll_sidebar.png", l.SidebarPos, SidebarSize);
            Header(prompt.Title, l.SidebarHeaderY, centre);
            var body = RaidUi.Paragraph("Body", _root, "scroll_body", new Vector2(centre + l.SidebarBody.x, l.SidebarBody.y),
                new Vector2(l.SidebarBodyWidth, l.SidebarItemSlot.y - l.SidebarBody.y - 6f), TextAlignmentOptions.TopLeft, UiKit.Neutral);
            body.text = prompt.Body ?? "";

            var hand = prompt.IndexOf(RaidOptionRole.Hand);
            if (hand >= 0)
            {
                var button = Choice("Hand", "byhand.png", l.ChoiceCorner(new Vector2(centre + l.SidebarInvestigate.x, l.SidebarInvestigate.y), ButtonArt.x), hand,
                    null, UiKit.Neutral, worded: false, tipWidth: l.SidebarInvestigateTooltipWidth);
                if (prompt.HandDisabled)
                {
                    // DD1 (curio_investigate_hand_disabled: #666): the hand of a curio that wants a quest item
                    button.interactable = false;
                    var colours = button.colors;
                    colours.disabledColor = Dd1Fonts.Colour("curio_investigate_hand_disabled", new Color(0.4f, 0.4f, 0.4f));
                    button.colors = colours;
                }
            }
            var pass = prompt.IndexOf(RaidOptionRole.Pass);
            if (pass >= 0)
                Choice("Pass", "pass.png", l.ChoiceCorner(new Vector2(centre + l.SidebarPass.x, l.SidebarPass.y), ButtonArt.x), pass,
                    null, UiKit.Neutral, worded: false, tipWidth: l.SidebarPassTooltipWidth);

            // DD1's slot for an item of the bag: its picture stands at item_slot_pos itself (seen in DD1's frames)
            var at = new Vector2(centre + l.SidebarItemSlot.x, l.SidebarItemSlot.y);
            _slot = RaidUi.Art("ItemSlot", _root, Dir + "use_inventory.png", at, l.ItemIconSize, new Color(0.06f, 0.06f, 0.06f), true);
            RaidUi.Pointer(_slot, () => _prompt?.SlotClicked?.Invoke(), null, inside =>
            {
                var words = _prompt?.SlotTooltip;
                if (inside && !string.IsNullOrEmpty(words)) _tooltip.Show(this, words, TopLeftOf(at + new Vector2(l.ItemIconSize.x, 0f)) + l.TooltipOffset, l.SidebarItemSlotTooltipWidth);
                else _tooltip.Hide(this);
            });
            // what lies in it from the start: DD1 shows the shovel in an obstacle's slot
            _slotCard = UiKit.Image("Card", _slot.transform, null);
            UiKit.Stretch((RectTransform)_slotCard.transform);
            RefreshSlot();
        }

        /// <summary>Keeps the card in the item slot in step with the bag: an obstacle's shovel is dark while none is carried.</summary>
        public void RefreshSlot()
        {
            if (_slotCard == null || _prompt == null) return;
            var run = DungeonRun.Current;
            var item = _prompt.SlotItem;
            var amount = item != null && run != null ? run.Bag.Count(item) : 0;
            var sprite = item != null ? InventoryContent.Icon(item, Mathf.Max(1, amount)) : null;
            _slotCard.enabled = sprite != null;
            _slotCard.sprite = sprite;
            _slotCard.color = amount > 0 ? Color.white : Dd1Fonts.Colour("inventory_unselectable", new Color(0.4f, 0.4f, 0.4f));
        }

        // ---- the basic scroll: hunger, plain questions -------------------------------------------------

        private void BuildBasic(RaidPrompt prompt)
        {
            var l = RaidLayout.Current;
            var centre = BasicSize.x * 0.5f;
            _root = Scroll("Scroll.Basic", "event_scroll_basic.png", l.BasicPos, BasicSize);
            Header(prompt.Title, l.BasicHeaderY, centre);

            if (prompt.Kind == RaidPromptKind.Hunger)
            {
                RaidUi.Art("Inset", _root, Dir + "inset_hunger.png", new Vector2(centre + l.BasicInset.x - InsetSize.x * 0.5f, l.BasicInset.y), InsetSize);
                var body = RaidUi.Paragraph("Body", _root, "scroll_body", new Vector2(centre, l.BasicBodyY), new Vector2(l.BasicBodyWidth, l.BasicOk.y - l.BasicBodyY - 4f), TextAlignmentOptions.Top, UiKit.Neutral);
                body.text = prompt.Body ?? "";

                // The two bowls are DD1's scroll buttons like the hand and the way past: no words under them with a
                // pointer (basic_scroll places them for a controller only: ok_controller_text_offset), DD1's
                // sentences with the numbers in their tooltips (ok_tooltip_width, cancel_tooltip_width).
                var eat = prompt.IndexOf(RaidOptionRole.Eat);
                var eatAt = l.ChoiceCorner(new Vector2(centre + l.BasicOk.x, l.BasicOk.y), ButtonArt.x);
                if (eat >= 0) Choice("Eat", "eat.png", eatAt, eat, null, UiKit.Notable, worded: false, tipWidth: l.BasicTooltipWidth);
                else if (prompt.EatMissing)
                {
                    // the bowl stays on the scroll, dark, and the pointer is told why (DD1's words for a meal the bag cannot give)
                    var bowl = Choice("Eat", "eat.png", eatAt, -1, null, UiKit.Harmful, worded: false, tipWidth: l.BasicTooltipWidth,
                        tip: "<color=" + RaidText.Hex(UiKit.Harmful) + ">" + RaidText.Get("str_meal_not_enough_provisions", "(Not Enough Food)") + "</color>", click: () => { });
                    bowl.interactable = false;
                }
                var starve = prompt.IndexOf(RaidOptionRole.Starve);
                if (starve >= 0) Choice("Starve", "starve.png", l.ChoiceCorner(new Vector2(centre + l.BasicCancel.x, l.BasicCancel.y), ButtonArt.x), starve, null, UiKit.Notable, worded: false, tipWidth: l.BasicTooltipWidth);
                return;
            }

            // A plain question: the text where hunger has its picture, the answers as DD1's lines of text
            // (basic_scroll.button_size) stacked up from button_y.
            var count = prompt.Options.Length;
            var rows = l.BasicButtonY - (count - 1) * (l.BasicButtonSize.y + RowGap);
            var text = RaidUi.Paragraph("Body", _root, "scroll_body", new Vector2(centre + PlainBody.x, PlainBody.y),
                new Vector2(l.BasicBodyWidth, Mathf.Min(PlainBodyHeight, rows - PlainBody.y - 8f)), TextAlignmentOptions.Top, UiKit.Neutral);
            text.text = prompt.Body ?? "";
            for (var i = 0; i < count; i++)
            {
                var index = i;
                var at = new Vector2(centre - l.BasicButtonSize.x * 0.5f, rows + i * (l.BasicButtonSize.y + RowGap));
                var row = UiKit.Image("Option" + i, _root, RaidUi.Sprite(Dir + "choice_button_frame.png"), new Color(1f, 1f, 1f, 0.9f), true);
                if (row.sprite == null) row.color = new Color(0f, 0f, 0f, 0.55f);
                ((RectTransform)row.transform).PlaceTopLeft(at, RaidUi.TopLeft, l.BasicButtonSize);
                var words = RaidUi.Label("Text", row.transform, "confirm_dialog_answer", new Vector2(l.BasicButtonSize.x * 0.5f, (l.BasicButtonSize.y - 28f) * 0.5f),
                    new Vector2(l.BasicButtonSize.x - 24f, 28f), TextAlignmentOptions.Top);
                RaidUi.Fit(words, 13f);
                words.text = prompt.Options[i];
                RaidUi.Pointer(row, () => Answered?.Invoke(index), null, inside => words.color = inside ? UiKit.Notable : UiKit.Neutral);
            }
        }

        // ---- the loot scroll --------------------------------------------------------------------------

        // Measured in DD1's own frames of the loot window (_lab/dd1_ref/raid/raid_loot_window_after_fight.png and
        // raid_loot_window_victory_two_items.png, 1920x1080): the scroll's picture stands with its top left corner
        // at 1120,200, which is where the sidebar scroll hangs (sidebar_scroll.pos 1348 200), not the basic one
        // (1342 140); byhand.png stands at 58,348 of the scroll and pass.png at 284,347, where overlay.loot.darkest
        // says take_all_pos 80 358 and close_pos 306 358: DD1 counts those 22 px in and 10 px down into the
        // pictures. Neither button has words under it: DD1 writes them for a controller only
        // (take_all_controller_text_offset) and tells the pointer in a tooltip.
        private static readonly Vector2 LootButtonNudge = new Vector2(-22f, -10f);

        /// <summary>
        /// DD1's loot window: a heading and a line of text by where the loot came from (<see cref="LootSource"/>),
        /// the cards, Take All and Close. A click on a card is <paramref name="take"/> with its index; the window
        /// does not close by itself.
        /// </summary>
        public void ShowLoot(string title, string description, IReadOnlyList<ItemStack> stacks, Action<int> take, Action takeAll, Action close)
        {
            Close();
            var l = RaidLayout.Current;
            _root = Scroll("Scroll.Loot", "event_scroll_loot.png", l.SidebarPos, LootSize);
            Header(title, l.LootTitle.y, l.LootTitle.x);
            var text = RaidUi.Paragraph("Description", _root, "scroll_body", l.LootDescription, new Vector2(l.LootDescriptionWidth, l.LootTilesY - l.LootDescription.y - 8f), TextAlignmentOptions.Top, UiKit.Neutral);
            text.text = description ?? "";

            var count = Mathf.Clamp(stacks.Count, 1, Mathf.Max(1, l.LootMaxItems));
            var width = (count - 1) * l.LootTileOffset + l.ItemIconSize.x;
            var left = l.LootTitle.x - width * 0.5f;
            if (count > l.LootMinItems)
            {
                // more cards than the scroll's own strip holds: DD1 lays a wider one under them, piece by piece
                var y = l.LootTilesY - l.LootBackdropY;
                RaidUi.Art("Strip.Left", _root, Dir + "event_scroll_loot_left_edge.png", new Vector2(left - LootEdge.x, y), LootEdge);
                for (var i = 0; i < count; i++)
                    RaidUi.Art("Strip." + i, _root, Dir + "event_scroll_loot_mid_section.png", new Vector2(left + i * l.LootTileOffset, y), new Vector2(i == count - 1 ? LootMid.x : l.LootTileOffset, LootMid.y));
                RaidUi.Art("Strip.Right", _root, Dir + "event_scroll_loot_right_edge.png", new Vector2(left + width, y), LootEdge);
            }
            _lootGrid = InventoryGrid.Build("Cards", _root, new Vector2(left, l.LootTilesY) - l.ItemIconOffset, count, count, new Vector2(l.LootTileOffset, l.ItemIconSize.y));
            _lootGrid.Clicked = (slot, right) => { if (!right && slot < _lootShown.Count && _lootShown[slot] >= 0) take?.Invoke(_lootShown[slot]); };
            _lootGrid.Hovered = slot =>
            {
                var stack = slot >= 0 && slot < _lootShown.Count && _lootShown[slot] >= 0 ? stacks[_lootShown[slot]] : null;
                if (stack == null || stack.Amount <= 0) _tooltip.Hide(this);
                else
                {
                    var card = _lootGrid.SlotRect(slot);
                    _tooltip.Show(this, RaidTooltip.Titled(InventoryText.Name(stack.Item), InventoryText.Tooltip(stack.Item, false)),
                        TopLeftOf(new Vector2(card.anchoredPosition.x + left - l.ItemIconOffset.x, l.LootTilesY + l.ItemIconSize.y)) + new Vector2(0f, 4f), l.ItemTooltipWidth);
                }
            };
            RefreshLoot(stacks);

            Choice("TakeAll", "byhand.png", l.LootTakeAll + LootButtonNudge, -1, "loot_take_all_text", UiKit.Notable, RaidText.Get("str_overlay_loot_take_all", "Take All"), takeAll, worded: false);
            Choice("Close", "pass.png", l.LootClose + LootButtonNudge, -1, "loot_close_text", UiKit.Notable, RaidText.Get("str_overlay_loot_close", "Close"), close, worded: false);
        }

        /// <summary>
        /// Shows what is still on the loot scroll: a card keeps its place while its stack lasts; a find of more
        /// kinds than DD1's scroll holds (loot_background.max_items) shows the rest as places come free.
        /// </summary>
        public void RefreshLoot(IReadOnlyList<ItemStack> stacks)
        {
            if (_lootGrid == null) return;
            for (var slot = 0; slot < _lootShown.Count; slot++)
                if (_lootShown[slot] >= stacks.Count || stacks[_lootShown[slot]].Amount <= 0) _lootShown[slot] = -1;
            for (var i = 0; i < stacks.Count; i++)
            {
                if (stacks[i].Amount <= 0 || _lootShown.Contains(i)) continue;
                var free = _lootShown.IndexOf(-1);
                if (free >= 0) _lootShown[free] = i;
                else if (_lootShown.Count < _lootGrid.SlotCount) _lootShown.Add(i);
            }
            for (var slot = 0; slot < _lootGrid.SlotCount; slot++)
            {
                var stack = slot < _lootShown.Count && _lootShown[slot] >= 0 ? stacks[_lootShown[slot]] : null;
                _lootGrid.Set(slot, stack?.Item, stack?.Amount ?? 0);
            }
        }

        public bool LootOpen => _lootGrid != null;

        // ---- parts --------------------------------------------------------------------------------------

        // A scroll hangs from the middle of its top edge (basic_scroll.pos, sidebar_scroll.pos).
        private RectTransform Scroll(string name, string art, Vector2 topCentre, Vector2 size)
        {
            var root = UiKit.Rect(name, _screen);
            root.PlaceTopLeft(new Vector2(topCentre.x - size.x * 0.5f, topCentre.y), RaidUi.TopLeft, size);
            // the scroll takes the clicks that miss its buttons: nothing behind it is clicked through it
            RaidUi.Art("Art", root, Dir + art, Vector2.zero, size, new Color(0.1f, 0.08f, 0.05f, 0.98f), true);
            return root;
        }

        private void Header(string title, float y, float centreX)
        {
            if (string.IsNullOrEmpty(title)) return;
            var header = RaidUi.Label("Header", _root, "scroll_header", new Vector2(centreX, y), new Vector2(HeaderWidth, HeaderHeight), TextAlignmentOptions.Top, UiKit.Notable);
            RaidUi.Fit(header, 24f);
            header.text = title;
        }

        // One of DD1's scroll buttons: its picture over the two rails of scrolls/choice_button_frame.png, both
        // with their corner at <paramref name="at"/> (RaidLayout.ChoiceCorner of the layout's position). With its
        // words under it (worded: a plain question's), the words are the answer's own unless DD1 has a word for
        // the button; the answer's own text is then what the pointer is told. Without them (DD1's scrolls and its
        // loot window under a pointer) the pointer is told <paramref name="tip"/>, else DD1's words for the
        // button, else the answer's own, else the caption the button would have had.
        private Button Choice(string name, string art, Vector2 at, int option, string style, Color colour, string caption = null, Action click = null, bool worded = true, float tipWidth = 0f, string tip = null)
        {
            var words = option >= 0 && _prompt != null ? _prompt.Options[option] : null;
            var told = option >= 0 && _prompt != null ? _prompt.Tooltips[option] : null;
            RaidUi.Art(name + ".Frame", _root, Dir + "choice_button_frame.png", at, ButtonArt);
            var button = RaidUi.ArtButton(name, _root, Dir + art, at, click ?? (() => Answered?.Invoke(option)), ButtonArt);
            if (worded) Caption(name + ".Caption", at, caption ?? words ?? "", style, colour);
            if (tip == null) tip = !worded ? (string.IsNullOrEmpty(told) ? words ?? caption : told) : caption != null && words != null ? words : told;
            if (string.IsNullOrEmpty(tip)) return button;
            var width = tipWidth > 0f ? tipWidth : RaidLayout.Current.ItemTooltipWidth;
            RaidUi.Pointer(button.targetGraphic, null, null, inside =>
            {
                // under the button, as the quest-complete choice's stand in DD1's frame (raid_quest_complete_return_tooltip.png)
                if (inside) _tooltip.Show(this, tip, TopLeftOf(at + new Vector2(ButtonArt.x * 0.5f, ButtonArt.y)) + new Vector2(0f, TipDrop), width, centred: true);
                else _tooltip.Hide(this);
            });
            return button;
        }

        private void Caption(string name, Vector2 buttonAt, string text, string style, Color colour)
        {
            var label = RaidUi.Label(name, _root, style, new Vector2(buttonAt.x + ButtonArt.x * 0.5f, buttonAt.y + ButtonArt.y - CaptionRise), new Vector2(CaptionWidth, 30f), TextAlignmentOptions.Top, colour);
            RaidUi.Fit(label, 12f);
            label.text = text;
        }

        // A point of the open scroll in the screen's pixels.
        private Vector2 TopLeftOf(Vector2 inScroll)
        {
            return new Vector2(_root.anchoredPosition.x, -_root.anchoredPosition.y) + inScroll;
        }
    }
}
