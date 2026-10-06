using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DD2Estate.Core;
using DD2Estate.Dd1;
using DD2Estate.Dungeon;
using DD2Estate.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD2Estate.Estate
{
    /// <summary>
    /// DD1's Districts screen, on DD1's layout (campaign/town/district/district.layout.darkest, its art beside
    /// it): the evening sky where a building's backdrop would stand, the screen's name between the two rules
    /// of its corner, the blueprints the estate holds at the sky's left edge, and a row of buildings that
    /// scrolls sideways in front of the town's roofs (which drift at half the pace). Each building stands on
    /// its own frame: the name on the frame's band, under it what the building gives, and while it is not
    /// built its price (gold, an heirloom, a blueprint) over DD1's button, which asks DD1's "Construct this
    /// building?" before anything is paid. A building that stands shows its finished picture on DD1's red
    /// frame. The buildings are DD1's own sprites (dlc/.../features/districts/fx/town_district_&lt;name&gt;, played
    /// by <see cref="SpineView"/>: "idle" the empty lot, "built"), their roots on the buildings' places, and a
    /// building just bought stands up under DD1's bricks and dust (fx/purchase_district, "purchased").
    ///
    /// How the layout's numbers are read: an entry's numbers count from the list window's corner
    /// (district_list_window_pos), an element's x is its middle and its y its top, as in the town crier's
    /// layout. Read so, the roofs (midground_y_offset) reach from the sky down into the dark ground, the
    /// tallest building ends under the name's rules, a building's foot (the skeleton's own 20 px over its
    /// place) leaves exactly the strip the scroll bar takes (district_list_scrollbar_offset) above the
    /// frames, the price's icons lie between the frame's two ornaments and the button sits on the lower one.
    /// The frames then hang 39 px below the backdrop's lower edge and end 11 px above the estate's bar: not
    /// seen in DD1 by whoever wrote this, so the first thing to hold against a DD1 frame.
    /// </summary>
    internal class DistrictsPanel : MonoBehaviour
    {
        private const string Dir = "campaign/town/district/";
        private const string LayoutFile = Dir + "district.layout.darkest";
        private const string TownLayout = "campaign/town/town.layout.darkest";
        private const string WidgetDir = "shared/widgets/";
        private const string IconDir = "shared/estate/";
        private const string BlueprintIcon = DistrictRules.Feature + "shared/estate/currency.blueprint.icon.png";

        // Sizes of DD1's art.
        private static readonly Vector2 FrameSize = new Vector2(1395f, 776f);    // district.background.png
        private static readonly Vector2 CloseSize = new Vector2(32f, 32f);       // progression_close.png
        private static readonly Vector2 Midground = new Vector2(1000f, 300f);    // district_midground.png
        private static readonly Vector2 SideBlend = new Vector2(20f, 660f);      // district_sideblend.png
        private static readonly Vector2 EntryFrame = new Vector2(330f, 315f);    // entry_frame.png
        private static readonly Vector2 BuiltFrame = new Vector2(330f, 280f);    // entry_frame_purchased.png
        private static readonly Vector2 SelectedFrame = new Vector2(330f, 58f);  // entry_frame_selected.png
        private static readonly Vector2 ButtonSize = new Vector2(64f, 32f);      // purchase_building_button.png
        private static readonly Vector2 IconSize = new Vector2(40f, 40f);        // currency.<heirloom>.icon.png, currency.blueprint.icon.png
        private static readonly Vector2 CoinSize = new Vector2(24f, 24f);        // currency.gold.icon.png
        private static readonly Vector2 ScrollArrow = new Vector2(25f, 41f);     // scrollbar_leftarrow.png
        private const float RailHeight = 21f;                                    // scrollbarhmid.png
        private const float RailCap = 12f;                                       // scrollbarleft.png, scrollbarright.png
        private const float PipSize = 24f;                                       // scrollpip.png
        // Measured on entry_frame.png: the body ends where the upper ornament begins (row 222), the purchased
        // frame's at its last row but one (256).
        private const float BodyEnd = 222f, BuiltBodyEnd = 256f;
        // DD1's "name" of a town screen: see ActivityLogPanel (the same widget, the same two rules in the backdrop).
        private static readonly Vector2 NameWidget = new Vector2(296f, 82f);
        private static readonly Vector2 NameSize = new Vector2(296f, 64f);
        // DD1's skeletons stand a building 20 px over its place (the bone "offset"): for a sheet whose skeleton cannot be read.
        private const float StockLift = 20f;
        // base.colours.darkest gives the unbuilt entries' details no colour but "darkness 0.4, saturation 0", which
        // the colour reader does not take: read as for an item that cannot be chosen (the same pair): 0.4 of white.
        private static readonly Color Grey = new Color(0.4f, 0.4f, 0.4f);
        // The mod's own: a built picture fades in where DD1's bricks and dust cannot be had; the wheel's step, the arrows'.
        private const float FadeIn = 0.8f;
        private const float WheelStep = 60f;
        private const float ArrowStep = 360f;
        private const float TipRoom = 4f;

        private static DistrictsPanel _instance;

        private TownPanel _panel;
        private RectTransform _frame, _window, _content, _roofs, _confirmFrame;
        private ScrollRect _scroll;
        private Dd1Tooltip _tooltip;
        private RaidConfirm _confirm;
        private TextMeshProUGUI _tally;
        private Vector2 _windowPos, _descAt, _costIconAt, _costNumberAt;
        private float _entryWidth, _entryGap, _buildingY, _frameY, _titleY, _descWidth, _costY, _costStep, _buyY, _roofSpeed, _roofY;
        private readonly Dictionary<string, RectTransform> _entries = new Dictionary<string, RectTransform>();
        private readonly Dictionary<string, Button> _buttons = new Dictionary<string, Button>();
        private string _shown, _fresh, _message, _asked;
        private CanvasGroup _freshArt;
        private GameObject _dust;
        private float _freshAt, _dustUntil;
        private bool _stale = true;

        public static bool IsOpen => _instance != null && TownPanel.Current != null && TownPanel.Current == _instance._panel;

        /// <summary>The bar's button.</summary>
        public static void Toggle()
        {
            if (IsOpen) TownPanel.Close();
            else Open();
        }

        public static bool Open()
        {
            if (!Districts.Available) return false;
            var panel = TownPanel.Open(Districts.Id, true);
            if (panel == null) return false;
            var view = panel.gameObject.AddComponent<DistrictsPanel>();
            _instance = view;
            view._panel = panel;
            view.Build();
            panel.Refresh = view.Refresh;
            view.Refresh();
            return true;
        }

        public static void Close()
        {
            if (IsOpen) TownPanel.Close();
        }

        private void OnEnable() => Districts.Changed += MarkStale;

        private void OnDisable() => Districts.Changed -= MarkStale;

        private void OnDestroy()
        {
            // the confirm question stands on the hamlet's canvas, over the roster too
            if (_confirmFrame != null) Destroy(_confirmFrame.gameObject);
            if (_instance == this) _instance = null;
        }

        private void MarkStale() => _stale = true;

        // ---- construction --------------------------------------------------------------------------------

        private void Build()
        {
            // FALLBACK numbers: DD1's own values of these entries, used when a file cannot be read.
            var town = Dd1Ui.Layout(TownLayout);
            var layout = Dd1Ui.Layout(LayoutFile);
            const string main = "district_layout", entry = "district_entry_layout";
            // town.layout.darkest has no place of the districts' own: their backdrop is as large as a building's
            // (and as the crier's and the log's, which stand at the same point) and is put where a building's is.
            var at = Dd1Ui.Offset(town, "town_background_layout", "area_pos", 144f, 132f);
            _frame = _panel.AddPart("Frame", at, FrameSize, TownPanel.Side.Centre);

            var back = Dd1Ui.Art("Backdrop", _frame, Dir + "district.background.png", Vector2.zero, FrameSize, new Color(0.03f, 0.03f, 0.035f, 0.97f), true);
            TownPanel.RightClickCloses(back);

            _windowPos = Dd1Ui.Offset(layout, main, "district_list_window_pos", 45f, 100f);
            var windowSize = Dd1Ui.Offset(layout, main, "district_list_window_size", 1308f, 1000f);
            _entryWidth = Dd1Ui.Number(layout, main, "entry_width", 350f);
            _entryGap = Dd1Ui.Number(layout, main, "entry_spacing", 10f);
            _roofSpeed = Dd1Ui.Number(layout, main, "midground_speed", 0.5f);
            _roofY = Dd1Ui.Number(layout, main, "midground_y_offset", 65f);
            var barY = Dd1Ui.Number(layout, main, "district_list_scrollbar_offset", 380f);
            _buildingY = Dd1Ui.Number(layout, entry, "building_y_offset", 400f);
            _frameY = Dd1Ui.Number(layout, entry, "frame_y_offset", 400f);
            _titleY = Dd1Ui.Number(layout, entry, "title_y_offset", 13f);
            _descAt = Dd1Ui.Offset(layout, entry, "desc_pos_offset", -144f, 56f);
            _descWidth = Dd1Ui.Number(layout, entry, "desc_window_width", 280f);
            _costY = Dd1Ui.Number(layout, entry, "cost_start_y_offset", 235f);
            _costIconAt = Dd1Ui.Offset(layout, entry, "cost_item_icon_offset", -110f, 0f);
            _costNumberAt = Dd1Ui.Offset(layout, entry, "cost_item_number_offset", 16f, 4f);
            _costStep = Dd1Ui.Number(layout, entry, "cost_item_entry_spacing", 100f);
            _buyY = Dd1Ui.Number(layout, entry, "cost_checkmark_y_offset", 40f);

            // The window on the row: as wide as DD1 says, as high as what stands in it (DD1 gives it 1000, more
            // than the screen has left; the bar's top cuts the panel off anyway).
            var buildings = Districts.Rules.Buildings;
            var height = Mathf.Min(windowSize.y, _frameY + EntryFrame.y + 4f);
            _window = UiKit.Rect("List", _frame).PlaceTopLeft(_windowPos, Dd1Ui.TopLeft, new Vector2(windowSize.x, height));
            var catcher = _window.gameObject.AddComponent<Image>();     // the wheel and a drag need something to land on
            catcher.color = new Color(0f, 0f, 0f, 0.02f);
            TownPanel.RightClickCloses(catcher);
            _window.gameObject.AddComponent<RectMask2D>();

            // The town's roofs behind the row: one picture after another, as far as the row can pull them.
            var width = Mathf.Max(windowSize.x, buildings.Count * _entryWidth + Mathf.Max(0, buildings.Count - 1) * _entryGap);
            _roofs = UiKit.Rect("Roofs", _window).PlaceTopLeft(new Vector2(0f, _roofY), Dd1Ui.TopLeft, new Vector2(width, Midground.y));
            var tiles = Mathf.CeilToInt((windowSize.x + (width - windowSize.x) * _roofSpeed) / Midground.x);
            for (var i = 0; i < tiles; i++) Dd1Ui.Art("Roofs" + i, _roofs, Dir + "district_midground.png", new Vector2(i * Midground.x, 0f), Midground);

            _content = UiKit.Rect("Row", _window).PlaceTopLeft(Vector2.zero, Dd1Ui.TopLeft, new Vector2(width, height));
            _scroll = _window.gameObject.AddComponent<ScrollRect>();
            _scroll.viewport = _window;
            _scroll.content = _content;
            _scroll.horizontal = true;
            _scroll.vertical = false;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            // Unity turns a wheel's step down into a push of the row to the right; the row should run on instead
            _scroll.scrollSensitivity = -WheelStep;
            _scroll.onValueChanged.AddListener(at2 => PlaceRoofs());

            for (var i = 0; i < buildings.Count; i++)
            {
                var column = UiKit.Rect("Entry." + buildings[i].Id, _content).PlaceTopLeft(new Vector2(i * (_entryWidth + _entryGap), 0f), Dd1Ui.TopLeft, new Vector2(_entryWidth, height));
                _entries[buildings[i].Id] = column;
            }

            // DD1's blend at the window's two edges: the row fades into the dark there.
            Dd1Ui.Art("BlendLeft", _frame, Dir + "district_sideblend.png", _windowPos, SideBlend);
            // the same picture turned round: its corner on the window's right edge, the picture running back from it
            var right = Dd1Ui.Art("BlendRight", _frame, Dir + "district_sideblend.png", _windowPos + new Vector2(windowSize.x, 0f), SideBlend);
            right.rectTransform.localScale = new Vector3(-1f, 1f, 1f);

            BuildScrollBar(new Vector2(_windowPos.x, _windowPos.y + barY), windowSize.x);

            Name(Dd1Ui.Offset(layout, main, "name_pos", -46f, -8f), DistrictText.ScreenName);
            Dd1Ui.ArtButton("Close", _frame, "shared/progression/progression_close.png", Dd1Ui.Offset(layout, main, "close_pos", 1352f, 12f), CloseSize, TownPanel.Close, "X");

            // The blueprints held: the icon's middle and top at currency_pos, the count beside it (the screen's
            // other currencies are other DLCs').
            var tallyAt = Dd1Ui.Offset(layout, main, "currency_pos", 32f, 130f);
            var tallyIcon = tallyAt + Dd1Ui.Offset(layout, main, "currency_icon_offset", 0f, 0f);
            var icon = Dd1Ui.Art("Blueprints.Icon", _frame, BlueprintIcon, tallyIcon - new Vector2(IconSize.x * 0.5f, 0f), IconSize, new Color(0.35f, 0.5f, 0.6f, 0.9f), true);
            _tally = Dd1Ui.Line("Blueprints", _frame, "town_district_currency", tallyAt + Dd1Ui.Offset(layout, main, "currency_number_offset", 20f, 5f), new Vector2(60f, 26f), TextAlignmentOptions.TopLeft,
                Dd1Fonts.Colour("town_district_blueprint_tally", UiKit.Notable));

            var tips = UiKit.Stretch(UiKit.Rect("Tips", _frame));
            _tooltip = new Dd1Tooltip(tips, FrameSize.x);
            UiKit.Hover(icon.gameObject, inside =>
            {
                if (!inside)
                {
                    _tooltip.Hide(icon);
                    return;
                }
                var about = DistrictText.BlueprintDescription;
                _tooltip.ShowAt(icon, tallyIcon + new Vector2(IconSize.x * 0.5f + 34f, 0f), DistrictText.Currency(DistrictRules.Blueprint, 1),
                    (about != null ? about + "\n" : "") + Dd1Ui.Tint("The estate holds " + Districts.Blueprints + ".", "town_building_info"));
            });
        }

        private void Name(Vector2 namePos, string text)
        {
            var centre = namePos + NameWidget;
            Dd1Ui.Line("Title", _frame, "town_name", new Vector2(centre.x, centre.y - NameSize.y * 0.5f), NameSize, TextAlignmentOptions.Center).text = text;
        }

        // DD1's bar for a row that scrolls sideways (shared/widgets: scrollbarhmid with its two ends, the red
        // pip, the two arrows), laid in the strip between the buildings' feet and their frames.
        private void BuildScrollBar(Vector2 at, float width)
        {
            var left = at.x + ScrollArrow.x;
            var length = width - 2f * ScrollArrow.x;
            Dd1Ui.ArtButton("ScrollLeft", _frame, WidgetDir + "scrollbar_leftarrow.png", new Vector2(at.x, at.y + (RailHeight - ScrollArrow.y) * 0.5f), ScrollArrow,
                () => ScrollBy(-ArrowStep), "<");
            Dd1Ui.ArtButton("ScrollRight", _frame, WidgetDir + "scrollbar_rightarrow.png", new Vector2(at.x + width - ScrollArrow.x, at.y + (RailHeight - ScrollArrow.y) * 0.5f),
                ScrollArrow, () => ScrollBy(ArrowStep), ">");
            var rail = Dd1Ui.Art("Rail", _frame, WidgetDir + "scrollbarhmid.png", new Vector2(left + RailCap, at.y), new Vector2(length - 2f * RailCap, RailHeight), new Color(0.12f, 0.11f, 0.1f, 0.9f), true);
            Dd1Ui.Art("RailLeft", _frame, WidgetDir + "scrollbarleft.png", new Vector2(left, at.y), new Vector2(RailCap, RailHeight));
            Dd1Ui.Art("RailRight", _frame, WidgetDir + "scrollbarright.png", new Vector2(left + length - RailCap, at.y), new Vector2(RailCap, RailHeight));

            var bar = rail.gameObject.AddComponent<Scrollbar>();
            bar.direction = Scrollbar.Direction.LeftToRight;
            var slide = UiKit.Rect("Slide", rail.transform).Stretch();
            var handle = UiKit.Rect("Handle", slide);
            handle.offsetMin = Vector2.zero;
            handle.offsetMax = Vector2.zero;
            var pip = UiKit.Image("Pip", handle, Dd1Ui.Sprite(WidgetDir + "scrollpip.png"), null, true);
            if (pip.sprite == null) pip.color = UiKit.Blood;
            ((RectTransform)pip.transform).Place(Dd1Ui.Middle, Dd1Ui.Middle, Vector2.zero, new Vector2(PipSize, PipSize));
            bar.targetGraphic = pip;
            bar.handleRect = handle;
            _scroll.horizontalScrollbar = bar;
            _scroll.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        }

        // How far the row can move, and how far it has (0: the first building at the window's left edge).
        private float Room => Mathf.Max(0f, _content.rect.width - _window.rect.width);

        private float Shift => Mathf.Clamp(-_content.anchoredPosition.x, 0f, Room);

        private void ScrollBy(float pixels)
        {
            if (Room <= 0f) return;
            _scroll.StopMovement();
            _scroll.horizontalNormalizedPosition = Mathf.Clamp01((Shift + pixels) / Room);
            PlaceRoofs();
        }

        // DD1's midground_speed: the roofs move at that share of the row's pace.
        private void PlaceRoofs()
        {
            if (_roofs != null) _roofs.anchoredPosition = new Vector2(-Shift * _roofSpeed, -_roofY);
        }

        // ---- state ---------------------------------------------------------------------------------------

        private void Refresh()
        {
            if (_frame == null) return;
            var key = new StringBuilder().Append(Districts.Unlocked).Append('|').Append(Districts.Blueprints).Append('|').Append(EstateState.Gold);
            foreach (var kind in EstateState.HeirloomIds) key.Append('|').Append(EstateState.Current.Heirloom(kind));
            foreach (var id in Districts.State.Built) key.Append('|').Append(id);
            if (!_stale && key.ToString() == _shown) return;
            _stale = false;
            _shown = key.ToString();

            _tally.text = Districts.Blueprints.ToString();
            _tooltip.Hide(null);
            _buttons.Clear();
            _freshArt = null;
            foreach (var building in Districts.Rules.Buildings)
            {
                var column = _entries[building.Id];
                UpgradeUi.Clear(column);
                Fill(column, building);
            }
            PlaceRoofs();
        }

        // One building: its picture on its place, its frame under it with the name, what it gives and, while it
        // is not built, the price over the button. Places are the column's pixels (the window's rows).
        private void Fill(RectTransform column, DistrictBuilding building)
        {
            var built = Districts.IsBuilt(building.Id);
            var middle = _entryWidth * 0.5f;
            var art = AddBuilding(column, building, built, new Vector2(middle, _buildingY));
            if (built && building.Id == _fresh && art != null) Raise(art, building);

            var size = built ? BuiltFrame : EntryFrame;
            var frame = Dd1Ui.Art("Frame", column, Dir + (built ? "entry_frame_purchased.png" : "entry_frame.png"), new Vector2(middle - size.x * 0.5f, _frameY), size,
                built ? new Color(0.14f, 0.04f, 0.04f, 0.92f) : new Color(0.03f, 0.03f, 0.035f, 0.95f), true);
            // the frame takes the pointer: a right click steps out as on the backdrop, a drag pulls the row
            TownPanel.RightClickCloses(frame);
            var inside = frame.rectTransform;
            var centre = size.x * 0.5f;

            // DD1 marks the entry under the pointer with two golden rules around the band of its name
            var lit = Dd1Ui.Art("Selected", inside, Dir + "entry_frame_selected.png", new Vector2(centre - SelectedFrame.x * 0.5f, 0f), SelectedFrame);
            lit.gameObject.SetActive(false);
            UiKit.Hover(frame.gameObject, over => lit.gameObject.SetActive(over));

            var title = Dd1Ui.Line("Name", inside, "town_district_entry_title", new Vector2(centre, _titleY), new Vector2(size.x - 24f, 30f), TextAlignmentOptions.Top,
                Dd1Fonts.Colour(built ? "town_district_entry_title_built" : "town_district_entry_title_not_built", built ? UiKit.Notable : UiKit.Neutral));
            title.text = DistrictText.Title(building);

            var lines = DistrictText.Lines(building);
            var details = Dd1Ui.Block("Gives", inside, "town_district_entry_details", new Vector2(centre + _descAt.x, _descAt.y), new Vector2(_descWidth, (built ? BuiltBodyEnd : BodyEnd) - _descAt.y - 4f),
                TextAlignmentOptions.TopLeft, built ? Dd1Fonts.Colour("town_district_entry_details_built", UiKit.Neutral) : Grey);
            // an entry that is not built is all grey in DD1: the classes' colour waits for the building
            details.text = string.Join("\n", built ? lines : lines.Select(ActivityLogText.Plain));
            if (built) return;

            var offer = Districts.Quote(building.Id);
            for (var i = 0; i < offer.Price.Count; i++) AddCost(inside, building, offer.Price[i], centre + _costIconAt.x + _costStep * i);

            var reason = Districts.Reason(offer);
            var buy = Dd1Ui.ArtButton("Build", inside, Dir + "purchase_building_button.png", new Vector2(centre - ButtonSize.x * 0.5f, _costY + _buyY), ButtonSize,
                () => Ask(building.Id), "OK");
            buy.interactable = offer.Valid;
            var colours = buy.colors;
            colours.disabledColor = Dd1Fonts.Colour("town_district_checkbox_disabled", new Color(0.4f, 0.4f, 0.4f, 0.8f));
            buy.colors = colours;
            _buttons[building.Id] = buy;
            // a button that cannot be pressed still says why
            var hit = buy.targetGraphic;
            UiKit.Hover(hit.gameObject, over =>
            {
                if (over && reason != null) Tip(hit, building.Id, _frameY + _costY + _buyY, null, Dd1Ui.Tint(reason + ".", "harmful"));
                else _tooltip.Hide(hit);
            });
        }

        // A currency of the price: its icon with the middle and the top at DD1's place, the amount beside it in
        // DD1's colour for a price that can be paid or one that cannot.
        private void AddCost(RectTransform frame, DistrictBuilding building, DistrictCost cost, float middle)
        {
            var gold = cost.Currency == DistrictRules.Gold;
            var size = gold ? CoinSize : IconSize;
            var file = gold ? IconDir + "currency.gold.icon.png" : cost.Currency == DistrictRules.Blueprint ? BlueprintIcon : IconDir + "currency." + cost.Currency + ".icon.png";
            var top = _costY + _costIconAt.y;
            // the coin is the smaller picture: it keeps the middle of the row the heirlooms fill
            var icon = Dd1Ui.Art("Cost." + cost.Currency, frame, file, new Vector2(middle - size.x * 0.5f, top + (IconSize.y - size.y) * 0.5f), size, UiKit.Parchment, true);
            var held = Districts.Held(cost.Currency);
            var enough = held >= cost.Amount;
            var amount = Dd1Ui.Line("Amount." + cost.Currency, frame, "town_district_cost_numbers", new Vector2(middle + _costNumberAt.x, top + _costNumberAt.y),
                new Vector2(Mathf.Max(30f, _costStep - _costNumberAt.x - IconSize.x * 0.5f), 26f), TextAlignmentOptions.TopLeft,
                Dd1Fonts.Colour(enough ? "town_district_cost_numbers" : "town_district_cost_numbers_insufficient", enough ? UiKit.Notable : UiKit.Harmful));
            // thousands set apart, as DD1 writes an amount ("1,000")
            amount.text = UpgradeUi.Amount(cost.Amount);
            UiKit.Hover(icon.gameObject, over =>
            {
                if (over)
                    Tip(icon, building.Id, _frameY + top, DistrictText.Currency(cost.Currency, 1),
                        Dd1Ui.Tint("The estate holds " + UpgradeUi.Amount(held) + ".", enough ? "town_building_info" : "harmful"));
                else _tooltip.Hide(icon);
            });
        }

        // A building just bought: DD1 plays the entry's second sprite over it on the same place (bricks and dust:
        // fx/purchase_district, "purchased") while the finished picture is there already. Where that sprite cannot
        // be had the finished picture fades in instead.
        private void Raise(RectTransform holder, DistrictBuilding building)
        {
            var since = Time.unscaledTime - _freshAt;
            var dust = SpineView.Create("Purchase", holder, Stem(building.PurchaseSkeleton));
            if (dust != null && dust.Has(building.PurchasedArt) && dust.Duration(building.PurchasedArt) > 0f)
            {
                // drawn again in the middle of it (the purse changed), the dust has settled
                if (since > 0.1f)
                {
                    Destroy(dust.gameObject);
                    return;
                }
                dust.Play(building.PurchasedArt, false);
                _dust = dust.gameObject;
                _dustUntil = _freshAt + dust.Duration(building.PurchasedArt);
                return;
            }
            if (dust != null) Destroy(dust.gameObject);
            _freshArt = holder.gameObject.AddComponent<CanvasGroup>();
            _freshArt.alpha = Mathf.Clamp01(since / FadeIn);
        }

        private static string Stem(string skeleton)
        {
            return !string.IsNullOrEmpty(skeleton) && skeleton.EndsWith(".skel", StringComparison.OrdinalIgnoreCase) ? skeleton.Substring(0, skeleton.Length - ".skel".Length) : null;
        }

        // The building's sprite in the state DD1 names for it (not_built_animation, built_animation: each shows
        // one picture), its root on the building's place. Returns the holder on that place, null when the install
        // has no art for the building.
        private RectTransform AddBuilding(RectTransform column, DistrictBuilding building, bool built, Vector2 place)
        {
            var state = built ? building.BuiltArt : building.NotBuiltArt;
            var stem = Stem(building.Skeleton);
            if (stem == null) return null;
            var holder = UiKit.Rect("Building", column).PlaceTopLeft(place, Dd1Ui.TopLeft, Vector2.zero);
            var view = SpineView.Create("Sprite", holder, stem);
            if (view != null)
            {
                view.Play(state, false);
                return holder;
            }

            // The sheet without its skeleton: the picture stands on its place, DD1's 20 px over it.
            var sprite = Dd1Ui.Sprite(stem + ".png#" + state);
            if (sprite == null)
            {
                Destroy(holder.gameObject);
                return null;
            }
            ((RectTransform)UiKit.Image(state, holder, sprite).transform).Place(Dd1Ui.Middle, new Vector2(0.5f, 0f), new Vector2(0f, StockLift), sprite.rect.size);
            return holder;
        }

        // A tooltip over a part of an entry: `top` is the part's top in the window's rows; the box opens above it.
        private void Tip(object owner, string buildingId, float top, string title, string body)
        {
            if (!_entries.TryGetValue(buildingId, out var column)) return;
            var left = _windowPos.x + _content.anchoredPosition.x + column.anchoredPosition.x + 10f;
            _tooltip.Show(owner, new Vector2(Mathf.Clamp(left, 8f, FrameSize.x - 340f), _windowPos.y + top - TipRoom), title, body);
        }

        // ---- the player's hand ---------------------------------------------------------------------------

        // DD1 asks before a building is paid for (str_district_build_building_confirm).
        private void Ask(string id)
        {
            _message = null;
            if (!Districts.Quote(id).Valid) return;
            _asked = id;
            if (_confirm == null)
            {
                _confirmFrame = RaidUi.Screen("DistrictsConfirm", RosterPanel.CanvasRoot);
                _confirm = new RaidConfirm(_confirmFrame);
            }
            _confirmFrame.SetAsLastSibling();
            _confirm.Ask(DistrictText.ConfirmQuestion, new[] { DistrictText.ConfirmYes, DistrictText.ConfirmNo }, answer =>
            {
                if (answer == 0) Construct(id);
            });
        }

        private void Construct(string id)
        {
            _message = Districts.Build(id);
            if (_message != null)
            {
                Plugin.Log.LogInfo("Districts: " + id + " not built: " + _message);
                return;
            }
            _fresh = id;
            _freshAt = Time.unscaledTime;
            _stale = true;
            Refresh();
        }

        private void Update()
        {
            if (_dust != null && Time.unscaledTime >= _dustUntil)
            {
                Destroy(_dust);
                _dust = null;
            }
            if (_freshArt == null) return;
            var shown = Mathf.Clamp01((Time.unscaledTime - _freshAt) / FadeIn);
            _freshArt.alpha = shown;
            if (shown >= 1f) _freshArt = null;
        }

        // ---- dev bridge ----------------------------------------------------------------------------------

        /// <summary>For tests: a click on a building's button (DD1's question comes up). Null, or why nothing was asked.</summary>
        public static string Press(string id)
        {
            if (!IsOpen) return "not open";
            if (Districts.Rules.Find(id) == null) return "no such building";
            var reason = Districts.Reason(Districts.Quote(id));
            if (reason != null) return reason;
            _instance.Show(id);
            _instance.Ask(id);
            return null;
        }

        /// <summary>For tests: the answer to DD1's question.</summary>
        public static string Answer(bool yes)
        {
            if (!IsOpen || _instance._confirm == null || !_instance._confirm.IsOpen) return "nothing is asked";
            _instance._confirm.Close();
            if (yes) _instance.Construct(_instance._asked);
            return _instance._message;
        }

        /// <summary>For tests: brings a building into the window (its column at the window's left edge, as far as the row goes).</summary>
        public static string ScrollTo(string id)
        {
            if (!IsOpen) return "not open";
            if (!_instance._entries.ContainsKey(id)) return "no such building";
            _instance.Show(id);
            return null;
        }

        private void Show(string id)
        {
            var room = Room;
            if (room <= 0f) return;
            _scroll.StopMovement();
            _scroll.horizontalNormalizedPosition = Mathf.Clamp01(_entries[id].anchoredPosition.x / room);
            PlaceRoofs();
        }

        public static object Snapshot()
        {
            if (!IsOpen) return new { open = false };
            var view = _instance;
            var entries = new List<object>();
            foreach (var building in Districts.Rules.Buildings)
            {
                var column = view._entries[building.Id];
                var left = view._windowPos.x + view._content.anchoredPosition.x + column.anchoredPosition.x;
                entries.Add(new
                {
                    id = building.Id,
                    built = Districts.IsBuilt(building.Id),
                    // DD1 screen pixels of the entry's column
                    left = left + view._frame.anchoredPosition.x,
                    inWindow = left + view._entryWidth > view._windowPos.x && left < view._windowPos.x + view._window.rect.width,
                    canBuild = view._buttons.TryGetValue(building.Id, out var button) && button.interactable,
                    parts = column.childCount
                });
            }
            return new
            {
                open = true,
                unlocked = Districts.Unlocked,
                blueprints = view._tally.text,
                shift = view.Shift,
                room = view.Room,
                asking = view._confirm != null && view._confirm.IsOpen,
                message = view._message,
                entries
            };
        }
    }
}
