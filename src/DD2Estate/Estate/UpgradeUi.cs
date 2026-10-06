using System;
using System.Collections.Generic;
using System.Globalization;
using Assets.Code.Actor;
using Assets.Code.Library;
using Assets.Code.UI.Managers;
using Assets.Code.Utils;
using DD2Estate.Dd1;
using DD2Estate.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DD2Estate.Estate
{
    /// <summary>
    /// Pieces the building screens share: DD1's step icons (bought, open, locked) with their gold backing and
    /// link, a hero's portrait in DD1's hero slot, prices as DD1 writes them, labels placed the DD1 way (pixels
    /// from the top left of the building's backdrop), and the frame DD1 gives the buildings that work on one
    /// hero at a time (<see cref="HeroActionFrame"/>). tools/preview_windows.py draws the same pieces with the
    /// same numbers.
    /// </summary>
    internal static class UpgradeUi
    {
        public const string BuildingsDir = "campaign/town/buildings/";
        public const string NodeDir = BuildingsDir + "upgrade/";
        public const string SlotDir = "campaign/town/hero_slot/";
        public const string GoldIcon = "shared/estate/currency.gold.icon.png";
        public const float NodeSize = 50f;
        public const float SlotSize = 85f;          // hero_slot.background.png
        // DD2's portraits are heads on clear ground with a wide margin; drawn larger than the slot and cut
        // off inside its frame they fill it the way DD1's faces do (the roster column does the same).
        private const float PortraitSize = 98f;
        private const float PortraitInset = 3f;
        private const string EstateLayout = "shared/estate/estate.layout.darkest";
        private const float AmountLine = 26f;       // Ubuntu small's 25 px line

        public static readonly Vector2 TopLeft = new Vector2(0f, 1f);
        public static readonly Color Dim = new Color(0.62f, 0.58f, 0.5f);
        public static readonly Color Warn = new Color(0.78f, 0.2f, 0.15f);
        public const string DimHex = "#9E9480";
        public const string WarnHex = "#C73326";
        public const string GoldHex = "#DBB55C";

        public enum NodeState { Bought, Open, Locked }

        private static DarkestFile _stepLayout, _estateLayout;
        private static bool _stepLayoutRead, _estateLayoutRead;

        /// <summary>DD1's small currency icon (gold and the four heirlooms).</summary>
        public static Sprite CurrencyIcon(string currency)
        {
            return Dd1Install.Sprite("shared/estate/currency." + currency + ".icon.png");
        }

        // ---- a step icon -------------------------------------------------------------------------------

        /// <summary>One step of a tree, a skill or a gear rank: DD1's icon with its backing and link.</summary>
        public class Node
        {
            public Image Back, Link, Icon, Highlight;
            public bool Linked = true;

            public void Set(NodeState state, bool highlight = false)
            {
                Back.gameObject.SetActive(state == NodeState.Bought);
                Link.gameObject.SetActive(state == NodeState.Bought && Linked);
                var sprite = NodeSprite(state);
                Icon.sprite = sprite;
                Icon.color = sprite != null ? Color.white : state == NodeState.Bought ? UiKit.Gold : state == NodeState.Open ? new Color(0.45f, 0.5f, 0.6f) : new Color(0.2f, 0.2f, 0.2f);
                Highlight.gameObject.SetActive(highlight);
            }
        }

        public static Sprite NodeSprite(NodeState state)
        {
            switch (state)
            {
                case NodeState.Bought: return Dd1Install.Sprite(NodeDir + "requirement_purchased_icon.png");
                case NodeState.Open: return Dd1Install.Sprite(NodeDir + "requirement_purchasable_icon.png");
                default: return Dd1Install.Sprite(NodeDir + "requirement_locked_icon.png");
            }
        }

        /// <summary>
        /// Builds a step whose 50 px icon has its corner at <paramref name="iconPos"/>. Backing and link sit
        /// where DD1's upgrade.layout.darkest puts them relative to the icon (the link reaches back to the
        /// step before).
        /// </summary>
        public static Node BuildNode(string name, Transform parent, Vector2 iconPos, Transform links = null)
        {
            var icon = StepOffset("icon_offset", 40f, 10f);
            var back = StepOffset("background_offset", 30f, 0f) - icon;
            var link = StepOffset("background_connector_offset", 0f, 24f) - icon;
            var node = new Node
            {
                // The connector reaches back under the step before it (or the tree's own icon): it is drawn
                // beneath all of them, and shows in the gaps only.
                Link = Art(name + ".Link", links ?? LinkLayer(parent), NodeDir + "requirement_purchased_background_connector.png", iconPos + link),
                Back = Art(name + ".Back", parent, NodeDir + "requirement_purchased_background.png", iconPos + back)
            };
            node.Icon = UiKit.Image(name, parent, null, Color.white, true);
            ((RectTransform)node.Icon.transform).PlaceTopLeft(iconPos, TopLeft, new Vector2(NodeSize, NodeSize));
            node.Highlight = Art(name + ".Highlight", parent, NodeDir + "requirement_highlight_overlay.png", iconPos, new Vector2(NodeSize, NodeSize));
            node.Set(NodeState.Locked);
            return node;
        }

        /// <summary>
        /// Where the connectors of a row's steps go: a layer under everything else of <paramref name="parent"/>
        /// (made on the first call). A parent with a backing of its own makes the layer itself, above the backing.
        /// </summary>
        public static Transform LinkLayer(Transform parent)
        {
            var layer = parent.Find("StepLinks");
            if (layer != null) return layer;
            var made = UiKit.Stretch(UiKit.Rect("StepLinks", parent));
            made.SetAsFirstSibling();
            return made;
        }

        /// <summary>An entry of DD1's upgrade_requirement_layout (the stock value if the file cannot be read).</summary>
        public static Vector2 StepOffset(string key, float x, float y)
        {
            return Offset(StepLayout, "upgrade_requirement_layout", key, x, y);
        }

        /// <summary>DD1's upgrade/upgrade.layout.darkest: a step's own numbers and the tooltip of a locked step. Null if it cannot be read.</summary>
        public static DarkestFile StepLayout
        {
            get
            {
                if (!_stepLayoutRead)
                {
                    _stepLayoutRead = true;
                    _stepLayout = DarkestFile.Load(NodeDir + "upgrade.layout.darkest");
                }
                return _stepLayout;
            }
        }

        public static Vector2 Offset(DarkestFile layout, string block, string key, float x, float y)
        {
            var entry = layout?.Find(block);
            return entry != null && entry.Values(key).Count >= 2 ? entry.Vector2(key) : new Vector2(x, y);
        }

        // ---- heroes and prices ---------------------------------------------------------------------------

        /// <summary>A hero's portrait in DD1's hero slot.</summary>
        public class Slot
        {
            public Image Back, Portrait;
            public RectTransform Rect => (RectTransform)Back.transform;

            /// <summary>Shows a hero's face, or the empty slot. The game drops portrait art it thinks nobody uses: asked for anew every time.</summary>
            public void Show(Sprite portrait, bool lit = false, bool grey = false)
            {
                Portrait.sprite = portrait;
                Portrait.enabled = portrait != null;
                Portrait.color = grey ? new Color(0.5f, 0.5f, 0.5f) : Color.white;
                var back = Dd1Ui.Sprite(SlotDir + (lit ? "hero_slot.backgroundhightlight.png" : "hero_slot.background.png"));
                if (back != null) Back.sprite = back;
            }
        }

        /// <summary>DD1's 85 px hero slot with its corner at <paramref name="topLeft"/>; the slot takes the pointer.</summary>
        public static Slot BuildSlot(string name, Transform parent, Vector2 topLeft)
        {
            var slot = new Slot { Back = Dd1Ui.Art(name, parent, SlotDir + "hero_slot.background.png", topLeft, new Vector2(SlotSize, SlotSize), new Color(0.07f, 0.07f, 0.07f), true) };
            var window = UiKit.Rect("Window", slot.Back.transform);
            window.PlaceTopLeft(new Vector2(PortraitInset, PortraitInset), TopLeft, new Vector2(SlotSize - 2f * PortraitInset, SlotSize - 2f * PortraitInset));
            window.gameObject.AddComponent<RectMask2D>();
            slot.Portrait = UiKit.Image("Portrait", window, null);
            slot.Portrait.preserveAspect = true;
            ((RectTransform)slot.Portrait.transform).Place(Dd1Ui.Middle, Dd1Ui.Middle, Vector2.zero, new Vector2(PortraitSize, PortraitSize));
            slot.Portrait.enabled = false;
            return slot;
        }

        /// <summary>
        /// A price as DD1 writes one, hung from DD1's cost position (<paramref name="costPos"/>: a layout's
        /// cost_offset, as the file has it). The row is centred on the position's x, and every currency stands
        /// to the position as shared/estate/estate.layout.darkest says. Gold (estate_currency_gold_layout): the
        /// 24 px coin from 12 px above it, which makes the position the coin's middle, and the amount 25 px on,
        /// from 14 px above. An heirloom (estate_currency_heirloom_layout): its 40 px icon from 10 px above,
        /// the amount 38 px on, from 4 px above, and the next heirloom 2 px after the amount. Icons at their
        /// own size, amounts with DD1's comma in the thousands. Returns the labels in the order of
        /// <paramref name="currencies"/>; their colour is the caller's to change (DD1:
        /// town_currency_cant_afford_amount for what the estate lacks).
        /// </summary>
        public static List<TextMeshProUGUI> BuildPrice(string name, Transform parent, Vector2 costPos, IReadOnlyList<string> currencies, IReadOnlyList<int> amounts, out RectTransform row)
        {
            if (!_estateLayoutRead)
            {
                _estateLayoutRead = true;
                _estateLayout = Dd1Ui.Layout(EstateLayout);
            }
            row = UiKit.Rect(name, parent);
            var labels = new List<TextMeshProUGUI>();
            var icons = new List<RectTransform>();
            float x = 0f, top = 0f, bottom = 0f;
            for (var i = 0; i < currencies.Count; i++)
            {
                // FALLBACK numbers: DD1's own values of these entries, used when the file cannot be read.
                var gold = currencies[i] == UpgradeRules.Gold;
                var block = gold ? "estate_currency_gold_layout" : "estate_currency_heirloom_layout";
                var iconAt = Dd1Ui.Offset(_estateLayout, block, "icon_offset", 0f, gold ? -12f : -10f);
                var numberAt = Dd1Ui.Offset(_estateLayout, block, "number_offset", gold ? 25f : 38f, gold ? -14f : -4f);
                var next = Dd1Ui.Offset(_estateLayout, block, "next_currency_spacing", gold ? 0f : 2f, 0f);

                var sprite = Dd1Ui.Sprite("shared/estate/currency." + currencies[i] + ".icon.png");
                var icon = UiKit.Image("Icon" + i, row, sprite, sprite != null ? Color.white : Color.clear);
                var iconSize = sprite != null ? sprite.rect.size : Vector2.zero;
                icons.Add(((RectTransform)icon.transform).PlaceTopLeft(new Vector2(x + iconAt.x, iconAt.y), TopLeft, iconSize));
                var label = UiKit.Text("Amount" + i, row, Amount(amounts[i]), "town_currency_amount", null, TextAlignmentOptions.TopLeft);
                label.textWrappingMode = TextWrappingModes.NoWrap;
                var width = Mathf.Ceil(label.GetPreferredValues(label.text).x);
                ((RectTransform)label.transform).PlaceTopLeft(new Vector2(x + numberAt.x, numberAt.y), TopLeft, new Vector2(width + 2f, AmountLine));
                labels.Add(label);

                top = Mathf.Min(top, Mathf.Min(iconAt.y, numberAt.y));
                bottom = Mathf.Max(bottom, Mathf.Max(iconAt.y + iconSize.y, numberAt.y + AmountLine));
                x += numberAt.x + width + (i < currencies.Count - 1 ? next.x : 0f);
            }
            // The row's rect holds all of it: its corner lies above the cost position by what hangs above that
            // (`top` is negative), and its children come down by as much.
            var down = new Vector2(0f, top);
            foreach (var icon in icons) icon.anchoredPosition += down;
            foreach (var label in labels) label.rectTransform.anchoredPosition += down;
            row.PlaceTopLeft(new Vector2(Mathf.Round(costPos.x - x * 0.5f), costPos.y + top), TopLeft, new Vector2(x, bottom - top));
            return labels;
        }

        /// <summary>
        /// A price in gold hung from DD1's cost position (<see cref="BuildPrice"/>: the 24 px coin and the amount
        /// centred on the position's height, the two together centred on its x; DD1's own picture of the
        /// Sanitarium, tutorial_popup.locking_pos_quirks.png, has the price so over the check mark).
        /// <paramref name="tooDear"/> writes it in DD1's colour for what the estate cannot afford. The one way
        /// the town's screens write a price in gold: over a slot of the Tavern and the Abbey, in the Sanitarium's
        /// frame, under a ware of the Nomad Wagon, beside a skill of the Guild and the Survivalist and a piece of
        /// the Blacksmith.
        /// </summary>
        public static RectTransform BuildGoldPrice(string name, Transform parent, Vector2 costPos, int amount, bool tooDear)
        {
            var labels = BuildPrice(name, parent, costPos, new[] { UpgradeRules.Gold }, new[] { amount }, out var row);
            if (tooDear) labels[0].color = Dd1Fonts.Colour("town_currency_cant_afford_amount", UiKit.Harmful);
            return row;
        }

        /// <summary>An amount as DD1 writes it, the thousands set apart: "1,750".</summary>
        public static string Amount(int amount) => amount.ToString("N0", CultureInfo.InvariantCulture);

        // ---- builders ----------------------------------------------------------------------------------

        /// <summary>
        /// A DD1 image at its own size (or <paramref name="size"/>). If the file is missing the image is hidden,
        /// or drawn as a flat <paramref name="fallback"/> box where something must stay visible.
        /// </summary>
        public static Image Art(string name, Transform parent, string dd1File, Vector2 topLeft, Vector2? size = null, Color? fallback = null)
        {
            var sprite = Dd1Install.Sprite(dd1File);
            var image = UiKit.Image(name, parent, sprite, sprite != null ? Color.white : fallback ?? Color.clear);
            ((RectTransform)image.transform).PlaceTopLeft(topLeft, TopLeft, size ?? (sprite != null ? sprite.rect.size : Vector2.zero));
            return image;
        }

        /// <summary>One line of text by size; it shrinks to its box rather than being cut.</summary>
        public static TextMeshProUGUI Label(string name, Transform parent, Vector2 topLeft, Vector2 size, float fontSize, Color color, TextAlignmentOptions align)
        {
            var label = UiKit.Text(name, parent, "", fontSize, color, align);
            ((RectTransform)label.transform).PlaceTopLeft(topLeft, TopLeft, size);
            Dd1Ui.NoCut(label, fontSize * 0.6f);
            return label;
        }

        /// <summary>A label that wraps and shrinks to stay inside its box.</summary>
        public static TextMeshProUGUI Paragraph(string name, Transform parent, Vector2 topLeft, Vector2 size, float fontSize, float smallest, Color color, TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
        {
            var label = UiKit.Text(name, parent, "", fontSize, color, align);
            ((RectTransform)label.transform).PlaceTopLeft(topLeft, TopLeft, size);
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Overflow;
            label.enableAutoSizing = true;
            label.fontSizeMin = smallest;
            label.fontSizeMax = fontSize;
            return label;
        }

        public static UpgradePointer Pointer(Graphic graphic, Action left, Action right = null, Action enter = null, Action exit = null)
        {
            graphic.raycastTarget = true;
            var pointer = graphic.gameObject.GetComponent<UpgradePointer>() ?? graphic.gameObject.AddComponent<UpgradePointer>();
            pointer.Left = left;
            pointer.Right = right;
            pointer.Enter = enter;
            pointer.Exit = exit;
            return pointer;
        }

        public static void Clear(Transform parent)
        {
            for (var i = parent.childCount - 1; i >= 0; i--)
            {
                var old = parent.GetChild(i).gameObject;
                old.SetActive(false);
                UnityEngine.Object.Destroy(old);
            }
        }

        /// <summary>
        /// DD2's character sheet as the estate's hero sheet: stats, quirks, and the skills, which are chosen there.
        /// What a hero wears is not changed on it (the Trinket Inventory in town, the raid panel in a dungeon).
        /// </summary>
        public static void ShowSheet(uint guid)
        {
            SingletonMonoBehaviour<CommonUiBhv>.Instance.ShowCharacterSheet(null, guid, isSkillEditable: true, isInventoryEditable: false, autoselectTrinketSlot: false, heroSelectFilterParty: false);
        }

        public static ActorInstance Hero(uint guid)
        {
            var actor = guid != 0u ? SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance.GetLibraryElement(guid) : null;
            return actor != null && actor.IsLiving ? actor : null;
        }
    }

    internal class UpgradePointer : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public Action Left, Right, Enter, Exit;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) Left?.Invoke();
            else if (eventData.button == PointerEventData.InputButton.Right) Right?.Invoke();
        }

        public void OnPointerEnter(PointerEventData eventData) => Enter?.Invoke();

        public void OnPointerExit(PointerEventData eventData) => Exit?.Invoke();
    }

    /// <summary>
    /// The frame DD1 gives the Guild, the Blacksmith and the Survivalist (hero_action.layout.darkest): behind
    /// everything the picture of the hero's class (heroes/&lt;class&gt;/&lt;class&gt;_guild_header.png), over it a
    /// banner with the slot a hero is dragged into and that hero's name (or DD1's "Drag a hero from the roster
    /// here."), under the banner at the left a column of DD1's text about the class in this building, and to
    /// its right the body the building fills. The hero comes from the roster column, by a drag onto the slot
    /// or a click on the row, or by the slot's own brackets (hero_slot.positive_prev / _next), which step
    /// through the roster.
    ///
    /// The mod's own has no place of its own in it: the hero's path and their standing in the building are
    /// told by the slot's tooltip, and a class DD1 has no text for gets the building's own facts in the column.
    /// </summary>
    internal class HeroActionFrame
    {
        private const string LayoutFile = UpgradeUi.BuildingsDir + "hero_action/hero_action.layout.darkest";
        private const string SlotLayoutFile = UpgradeUi.SlotDir + "hero_slot.layout.darkest";
        private static readonly Vector2 HeaderSize = new Vector2(715f, 630f);       // <class>_guild_header.png
        private static readonly Vector2 BracketSize = new Vector2(24f, 74f);        // hero_slot.positive_prev.png, hero_slot.positive_next.png
        private const float VerboseFrameHeight = 478f;                              // verbose_frame.png: its rule runs down the text's left

        // The hero last worked on stays in the slot from one of the three buildings to the next.
        private static uint _last;

        private readonly RosterWindow _window;
        private readonly string _building;
        private readonly UpgradeUi.Slot _slot;
        private readonly HeroSlotHover _over;
        private readonly Image _header, _accept, _prev, _next;
        private readonly HeroSlotBrackets _brackets;
        private readonly RectTransform _leaving;
        private readonly TextMeshProUGUI _name, _help, _verboseTitle, _verboseBody;
        private readonly Vector2 _slotPos;
        private string _headerOf, _standing, _extra;

        /// <summary>The hero in the slot; 0 while it is empty.</summary>
        public uint Selected { get; private set; }

        /// <summary>Called when another hero takes the slot.</summary>
        public Action Picked;

        /// <summary>DD1's hero_action_layout body_pos in the backdrop's pixels: what the building's own layout counts from.</summary>
        public Vector2 BodyPos { get; }

        /// <param name="verboseWidth">Width of the text column; 0 = DD1's own (body_text_width).</param>
        public HeroActionFrame(RosterWindow window, string building, float verboseWidth = 0f)
        {
            _window = window;
            _building = building;
            var frame = window.Frame;
            // FALLBACK numbers: DD1's own values of these entries, used when the file cannot be read.
            var layout = Dd1Ui.Layout(LayoutFile);
            var slotLayout = Dd1Ui.Layout(SlotLayoutFile);
            const string banner = "hero_action_banner_layout", verbose = "hero_action_verbose_layout", slot = "town_hero_slot_layout";
            var origin = window.Body + Dd1Ui.Offset(layout, "hero_action_layout", "base_pos", 220f, 44f);
            var bannerPos = origin + Dd1Ui.Offset(layout, "hero_action_layout", "banner_pos", 0f, 0f);
            var verbosePos = origin + Dd1Ui.Offset(layout, "hero_action_layout", "verbose_pos", 0f, 105f);
            BodyPos = origin + Dd1Ui.Offset(layout, "hero_action_layout", "body_pos", 240f, 121f);
            // The slot's offset counts from the body: read so, it lands in the left end of the box drawn in the backdrop.
            _slotPos = BodyPos + Dd1Ui.Offset(layout, banner, "hero_slot_offset", -230f, -100f);
            var namePos = bannerPos + Dd1Ui.Offset(layout, banner, "name_offset", 105f, 45f);
            var helpPos = bannerPos + Dd1Ui.Offset(layout, banner, "help_offset", 100f, 46f);
            var closePos = bannerPos + Dd1Ui.Offset(layout, banner, "close_button_offset", 680f, 0f);
            var textWidth = verboseWidth > 0f ? verboseWidth : Dd1Ui.Number(layout, verbose, "body_text_width", 260f);
            var titlePos = verbosePos + Dd1Ui.Offset(layout, verbose, "title_text_offset", 5f, 32f);
            var bodyPos = verbosePos + Dd1Ui.Offset(layout, verbose, "body_text_offset", 5f, 74f);

            // The class's picture lies right over the backdrop and the keeper and under everything else of the
            // screen: it is black where it is not painted, and hides the empty banner's box drawn in the backdrop.
            _header = UiKit.Image("ClassArt", frame, null, Color.clear);
            ((RectTransform)_header.transform).PlaceTopLeft(bannerPos + Dd1Ui.Offset(layout, banner, "header_offset", -10f, 0f), UpgradeUi.TopLeft, HeaderSize);
            _header.enabled = false;
            var under = frame.Find("Keeper") ?? frame.Find("Backdrop");
            _header.transform.SetSiblingIndex(under != null ? under.GetSiblingIndex() + 1 : 0);

            // The banner's close_button_offset would put the way out two pixels lower than the building's own
            // close_pos; DD1's frames of the Guild and the Blacksmith have the button at the building's place
            // (1496, 144, found by matching progression_close.png), so the window's button stays where it is.

            _slot = UpgradeUi.BuildSlot("HeroSlot", frame, _slotPos);
            UpgradeUi.Pointer(_slot.Back, null, () => { if (Selected != 0u) UpgradeUi.ShowSheet(Selected); }, () => SlotTip(true), () => SlotTip(false));
            // DD1's look of a slot that will take the hero held over it: the lit ground and the gold frame.
            _accept = Dd1Ui.Art("Accept", _slot.Back.transform, UpgradeUi.SlotDir + "hero_slot.positive_frame.png", Vector2.zero, new Vector2(UpgradeUi.SlotSize, UpgradeUi.SlotSize));
            _accept.gameObject.SetActive(false);
            _over = _slot.Back.gameObject.AddComponent<HeroSlotHover>();
            _over.Changed = over => ShowSlot(Hero);
            // Over the slot: the face of the hero who gives it up, on its way out.
            _leaving = UiKit.Stretch(UiKit.Rect("HeroLeaving", frame));
            // The slot's brackets stand on its left and right edge while a hero is in it.
            _prev = Bracket("HeroPrev", frame, "hero_slot.positive_prev.png", _slotPos + Dd1Ui.Offset(slotLayout, slot, "prev_offset", -15f, 4f), -1);
            _next = Bracket("HeroNext", frame, "hero_slot.positive_next.png", _slotPos + Dd1Ui.Offset(slotLayout, slot, "next_offset", 76f, 4f), 1);
            _brackets = _slot.Back.gameObject.AddComponent<HeroSlotBrackets>();
            _brackets.Prev = _prev.gameObject;
            _brackets.Next = _next.gameObject;

            var nameWidth = closePos.x - namePos.x - 12f;
            _name = Dd1Ui.Line("HeroName", frame, "town_action_banner_name", namePos, new Vector2(nameWidth, 64f));
            // DD1 renames a hero on its character sheet; the estate's sheet is DD2's, so the name is written here
            HeroRename.Attach(_name, () => Selected, window);
            _help = Dd1Ui.Line("HeroHelp", frame, "town_action_banner_help", helpPos, new Vector2(nameWidth, 42f));
            // The empty banner's own line; "Drag a hero here." (str_empty_hero_slot_action) is the empty slot's tooltip.
            _help.text = WindowText.Plain("action_select_hero") ?? "Drag a hero from the roster here.";

            var column = Dd1Ui.Art("VerboseFrame", frame, UpgradeUi.BuildingsDir + "hero_action/verbose_frame.png", verbosePos + Dd1Ui.Offset(layout, verbose, "frame_offset", -20f, 0f));
            var columnHeight = column.sprite != null ? column.sprite.rect.height : VerboseFrameHeight;
            _verboseTitle = Dd1Ui.Line("VerboseTitle", frame, "town_action_verbose_title_text", titlePos, new Vector2(textWidth, 30f));
            _verboseBody = Dd1Ui.Block("VerboseBody", frame, "town_action_verbose_body_text", bodyPos, new Vector2(textWidth, columnHeight - (bodyPos.y - verbosePos.y)));
            // DD1 writes the text 1:1: it wraps at body_text_width and is not made smaller to fit.
            _verboseBody.enableAutoSizing = false;

            window.HeroPicked = Pick;
            window.AddDrop(_slot.Rect, Pick);
            Selected = UpgradeUi.Hero(_last) != null ? _last : 0u;
        }

        public ActorInstance Hero => UpgradeUi.Hero(Selected);

        private Image Bracket(string name, Transform parent, string file, Vector2 at, int step)
        {
            var image = Dd1Ui.Art(name, parent, UpgradeUi.SlotDir + file, at, BracketSize);
            if (image.sprite != null)
            {
                UpgradeUi.Pointer(image, () =>
                {
                    DD2Estate.Dd2.EstateAudio.Ui("ui/town/button_click");
                    Step(step);
                });
                // DD1 draws a picture that is a button brighter under the pointer (button_highlight).
                image.gameObject.AddComponent<DD2Estate.Dungeon.RaidHighlight>();
            }
            image.gameObject.SetActive(false);
            return image;
        }

        // The slot's brackets: the hero before or after this one in the roster column, round and round.
        private void Step(int by)
        {
            var order = HeroActionUi.RosterOrder();
            if (order.Count == 0) return;
            var at = order.IndexOf(Selected);
            Pick(order[at < 0 ? (by > 0 ? 0 : order.Count - 1) : ((at + by) % order.Count + order.Count) % order.Count]);
        }

        /// <summary>Puts a hero in the slot (a click on their roster row, a drag onto the slot, the slot's brackets).</summary>
        public void Pick(uint guid)
        {
            if (UpgradeUi.Hero(guid) == null || Selected == guid) return;
            // DD1 lets the face of the hero who gives up the slot slide out of it (hero_slot.layout.darkest, replace_slide_out_anim).
            if (Selected != 0u)
            {
                try { HeroSlots.SlideOut(_slot, _leaving); }
                catch (Exception e) { Plugin.Log.LogWarning("Hamlet: the face leaving the hero slot could not be shown: " + e.Message); }
            }
            Selected = guid;
            _last = guid;
            _window.Say(null);
            try { Picked?.Invoke(); }
            catch (Exception e) { Plugin.Log.LogError("Hamlet: picking a hero failed: " + e); }
        }

        /// <summary>Keeps the banner, the class's picture and the text column in step with the hero; empties the slot when the hero is gone.</summary>
        public void Refresh()
        {
            var hero = Hero;
            if (hero == null) Selected = 0u;
            ShowSlot(hero);
            ShowHeader(hero);
            // DD1 shows the brackets only with the pointer on the slot (its own Guild with a hero in: none at rest)
            _brackets.Has = hero != null;
            _name.gameObject.SetActive(hero != null);
            _help.gameObject.SetActive(hero == null);
            _verboseTitle.gameObject.SetActive(hero != null);
            _verboseBody.gameObject.SetActive(hero != null);
            if (hero == null) return;
            _name.text = hero.ActorName;
            // DD1 heads the column with the class (hero_class_name_<class>); a class DD1 never had keeps DD2's name for it.
            _verboseTitle.text = HeroActionUi.Words("hero_class_name_" + HeroActionUi.ClassOf(hero)) ?? HeroNames.ClassName(hero);
            ShowVerbose(hero);
        }

        // The game drops portrait art it thinks nobody uses: asked for anew every time.
        private void ShowSlot(ActorInstance hero)
        {
            var held = _over != null && _over.Over;
            _slot.Show(hero != null ? HeroNames.Portrait(hero) : null, held);
            _accept.gameObject.SetActive(held);
        }

        private void ShowHeader(ActorInstance hero)
        {
            var cls = hero != null ? HeroActionUi.ClassOf(hero) : null;
            if (cls == _headerOf) return;
            _headerOf = cls;
            var file = hero != null ? HeroActionUi.ClassFile(hero, cls + "_guild_header.png") : null;
            var art = file != null ? Dd1Install.Sprite(file) : null;
            _header.sprite = art;
            _header.color = Color.white;
            _header.enabled = art != null;
            if (art != null) ((RectTransform)_header.transform).sizeDelta = art.rect.size;
        }

        // An empty slot says DD1's "Drag a hero here."; a hero's slot says what DD1 has no place for: the
        // hero's path beside the class, and their standing in this building.
        private void SlotTip(bool show)
        {
            var hero = show ? Hero : null;
            var at = _slotPos + new Vector2(0f, UpgradeUi.SlotSize + 4f);
            if (!show) _window.Tip(_slot, false, Vector2.zero, null, null);
            else if (hero == null) _window.Tip(_slot, true, at, null, WindowText.Plain("str_empty_hero_slot_action") ?? "Drag a hero here.");
            else _window.Tip(_slot, true, at, HeroNames.Title(hero), string.IsNullOrEmpty(_extra) || string.IsNullOrEmpty(Blurb(hero)) ? _standing : _standing + "\n" + _extra);
        }

        /// <summary>
        /// The hero's standing in this building, which DD1's screen has no place for: <paramref name="line"/> is
        /// told by the slot's tooltip, and so is <paramref name="more"/>, unless DD1 has no text about the class
        /// in this building: then it stands in the text column instead.
        /// </summary>
        public void Describe(string line, string more = null)
        {
            _standing = line ?? "";
            _extra = more;
            var hero = Hero;
            if (hero != null) ShowVerbose(hero);
        }

        // What DD1 writes about the class in this building, and nothing else; DD2's classes DD1 never had get the mod's facts.
        private void ShowVerbose(ActorInstance hero)
        {
            var blurb = Blurb(hero);
            _verboseBody.text = string.IsNullOrEmpty(blurb) ? _extra ?? "" : blurb;
        }

        private string Blurb(ActorInstance hero)
        {
            return HeroActionUi.Words("action_verbose_body_" + _building + "_" + HeroActionUi.ClassOf(hero));
        }
    }
}
