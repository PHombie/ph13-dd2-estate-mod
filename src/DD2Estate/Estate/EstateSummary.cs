using System;
using System.Collections.Generic;
using System.Globalization;
using DD2Estate.Dd1;
using DD2Estate.Dd2;
using DD2Estate.UI;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;

namespace DD2Estate.Estate
{
    /// <summary>
    /// The estate's bar along the bottom of the hamlet, as DD1 lays it out: the pile of gold (DD1's own sprite:
    /// it sparkles, and it flares when gold is paid) with its count in the large font, then the four heirlooms,
    /// each a small icon with its count and DD1's tooltip. The bar is DD1's progression bar; the hamlet puts its
    /// Embark button on the middle of it.
    ///
    /// Places come from town.layout.darkest (estate_summary_pos), estate_summary/estate_summary.layout.darkest
    /// (the strip) and shared/estate/estate.layout.darkest (icon and number of one currency); the order of the
    /// heirlooms from campaign/estate/estate.json. The stock values stand in when a file cannot be read.
    /// tools/preview_hamlet.py draws the same layout offline: change one, change the other.
    ///
    /// How DD1 seats a currency was measured on a real frame of its hamlet (1920x1080, the Color of Madness'
    /// layout with currency_pos 180 42): icon and count are one block, from icon_offset.x to number_offset.x plus
    /// the count's width, and that block's middle stands on the currency's place. The heirlooms' icons are 31 px
    /// left of their places beside a count of two figures (38 + 24, halved) and 25 px beside the shard's "0"
    /// (38 + 12, halved); the gold's "4,990", 86 px wide there, begins 2 px right of its place (90 - (90 + 86) / 2).
    /// A count's width is the sum of its glyphs' advances in DD1's font.
    /// </summary>
    internal class EstateSummary : MonoBehaviour
    {
        private const string TownLayout = "campaign/town/town.layout.darkest";
        private const string SummaryLayout = "campaign/town/estate_summary/estate_summary.layout.darkest";
        private const string CurrencyLayout = "shared/estate/estate.layout.darkest";
        private const string CurrencyTable = "campaign/estate/estate.json";
        private const string BarArt = "shared/progression/progression_bar.png";
        private const string IconDir = "shared/estate/";
        // DD1's sprite of the pile (Spine): "idle" twinkles round and round, "spend" throws rays and coins once.
        private const string PileSprite = "fx/estate_gold_pile/estate_gold_pile.sprite";
        private const string PileIdle = "idle", PileSpend = "spend";

        private const float ScreenHeight = 1080f;
        private const float BarHeight = 138f;                                    // progression_bar.png (1920x138)
        private static readonly Vector2 GoldIconSize = new Vector2(88f, 88f);    // currency.gold.large_icon.png
        private static readonly Vector2 HeirloomIconSize = new Vector2(40f, 40f);
        private const float PileHalfWidth = 47f;                                 // the sprite draws its pile 94 wide
        private const float GoldSmallest = 28f;                                  // a count too long for its place shrinks before it reaches the pile
        private const float RefreshSeconds = 0.1f;
        // The mod's own number: the widest line of a tooltip.
        private const float TipWidth = 460f;
        // FALLBACK, used only when campaign/estate/estate.json cannot be read: DD1's own order.
        private static readonly string[] StockOrder = { "bust", "portrait", "deed", "crest" };

        private static readonly Vector2 TopLeft = new Vector2(0f, 1f);
        private static readonly Vector2 Middle = new Vector2(0.5f, 0.5f);

        private class Heirloom
        {
            public string Id;
            /// <summary>The currency's place on the bar (bar pixels): its icon and count are centred on it.</summary>
            public Vector2 Place;
            public RectTransform Icon;
            public TextMeshProUGUI Amount;
            public int Shown = -1;
        }

        private TextMeshProUGUI _gold;
        private SpineView _pile;
        private readonly List<Heirloom> _heirlooms = new List<Heirloom>();
        private Vector2 _origin, _goldPlace, _goldAt, _iconAt, _numberAt, _tipAt;
        private float _goldRoom, _goldSize;
        private RectTransform _tipFrame;
        private Dd1Tooltip _tooltip;
        private int _goldShown = -1;
        private float _nextRefresh;

        private static EstateSummary _instance;
        private static int? _goldInstead;

        /// <summary>
        /// A count the bar shows in place of the purse's: the provision screen's purse less what stands on its
        /// bill, as DD1's gold goes down with every purchase. Written and seated by the bar's own rule, and the
        /// pile flares as it drops. Null: the purse again.
        /// </summary>
        public static void ShowGold(int? amount)
        {
            _goldInstead = amount;
            if (_instance != null) _instance._nextRefresh = 0f;
        }

        /// <summary>For tests: the gold count as the bar writes it.</summary>
        public static string GoldShown => _instance != null && _instance._gold != null ? _instance._gold.text : null;

        public static EstateSummary Build(Transform canvasRoot)
        {
            var town = Load(TownLayout);
            var strip = Load(SummaryLayout);
            var currency = Load(CurrencyLayout);
            const string summary = "estate_summary_layout";
            var origin = HamletScreen.Offset(town, "town_screen_layout", "estate_summary_pos", 0f, 975f)
                         + HamletScreen.Offset(strip, summary, "pos_offset", 0f, -17f);

            // A strip across the foot of the screen whose top-left corner is DD1's: children count from there.
            var root = UiKit.Rect("EstateSummary", canvasRoot);
            root.anchorMin = new Vector2(0f, 0f);
            root.anchorMax = new Vector2(1f, 0f);
            root.pivot = TopLeft;
            root.offsetMin = new Vector2(origin.x, 0f);
            root.offsetMax = new Vector2(0f, ScreenHeight - origin.y);
            var view = root.gameObject.AddComponent<EstateSummary>();
            _instance = view;
            view._origin = origin;

            // The bar spans the screen and takes the clicks of the town's foot behind it. Its art is a little
            // taller than what is left of the screen: the black foot of it hangs below the edge, as in DD1.
            var sprite = Dd1Install.Found ? Dd1Install.Sprite(BarArt) : null;
            var bar = UiKit.Image("Bar", root, sprite, sprite != null ? Color.white : new Color(0f, 0f, 0f, 0.85f), true);
            var barRect = (RectTransform)bar.transform;
            barRect.anchorMin = new Vector2(0f, 1f);
            barRect.anchorMax = new Vector2(1f, 1f);
            barRect.pivot = new Vector2(0.5f, 1f);
            barRect.offsetMin = new Vector2(0f, -BarHeight);
            barRect.offsetMax = Vector2.zero;

            var at = HamletScreen.Offset(strip, summary, "currency_pos", 200f, 42f);

            // Gold. DD1's bar draws no icon for it but the pile, a sprite of its own whose middle lies gold_pile_offset
            // from the currency's place (on the frame: the pile's picture at 0.75 of the sheet's, its middle at 115.5, 995).
            var pileAt = at + HamletScreen.Offset(strip, summary, "gold_pile_offset", -65f, -5f);
            view._pile = SpineView.Create("GoldPile", root, PileSprite);
            if (view._pile != null)
            {
                view._pile.Rect.PlaceTopLeft(pileAt, Middle, Vector2.zero);
                view._pile.Play(PileIdle);
            }
            else
            {
                // FALLBACK: the still picture of the pile, for an install whose sprite cannot be read.
                UiKit.Art("GoldIcon", root, IconDir + "currency.gold.large_icon.png", pileAt - GoldIconSize * 0.5f, GoldIconSize, UiKit.Gold);
            }

            // The count: the gold's block runs from its icon's place (icon_offset.x 0; the bar draws the pile instead) to
            // number_offset.x plus the count's width, centred on the currency's place like an heirloom's. The frame's
            // "4,990" (86 px in its font) begins at 182 = 180 - (90 + 86) / 2 + 90, glyph for glyph.
            const string large = "estate_large_currency_layout";
            view._goldPlace = at;
            view._goldAt = HamletScreen.Offset(currency, large, "number_offset", 90f, -16f);
            // a count too long for DD1's place shrinks before it reaches the pile
            view._goldRoom = Mathf.Max(60f, 2f * (at.x + view._goldAt.x * 0.5f - (pileAt.x + PileHalfWidth)));
            view._gold = UiKit.Text("GoldAmount", root, "0", "town_large_currency_amount", Dd1Fonts.Colour("town_currency_amount", UiKit.Gold), TextAlignmentOptions.TopLeft);
            view._gold.textWrappingMode = TextWrappingModes.NoWrap;
            view._gold.overflowMode = TextOverflowModes.Overflow;
            view._goldSize = view._gold.fontSize;
            ((RectTransform)view._gold.transform).PlaceTopLeft(at + view._goldAt, TopLeft, new Vector2(400f, 64f));
            at += HamletScreen.Offset(strip, summary, "large_currency_spacing", 200f, 0f);

            const string small = "estate_currency_heirloom_layout";
            view._iconAt = HamletScreen.Offset(currency, small, "icon_offset", 0f, -10f);
            view._numberAt = HamletScreen.Offset(currency, small, "number_offset", 38f, -4f);
            view._tipAt = HamletScreen.Offset(strip, summary, "tooltip_offset", 0f, -48f);
            var step = HamletScreen.Offset(strip, summary, "currency_spacing", 74f, 0f);
            foreach (var id in HeirloomOrder())
            {
                var heirloom = new Heirloom { Id = id, Place = at };
                // DD1: a click on an heirloom opens the exchange with that heirloom picked.
                var icon = UiKit.Art(id + "Icon", root, IconDir + "currency." + id + ".icon.png", at + view._iconAt, HeirloomIconSize, UiKit.Parchment, true);
                var kind = id;
                UpgradeUi.Pointer(icon, () => HeirloomExchangePanel.Open(kind));
                heirloom.Icon = (RectTransform)icon.transform;
                heirloom.Amount = UiKit.Text(id + "Amount", root, "0", "town_currency_amount", null, TextAlignmentOptions.TopLeft);
                heirloom.Amount.textWrappingMode = TextWrappingModes.NoWrap;
                heirloom.Amount.raycastTarget = true;
                ((RectTransform)heirloom.Amount.transform).PlaceTopLeft(at + view._numberAt, TopLeft, new Vector2(60f, 26f));
                // DD1 says what an heirloom is and how many there are while the pointer rests on it.
                UiKit.Hover(icon.gameObject, inside => view.Tip(heirloom, inside));
                UiKit.Hover(heirloom.Amount.gameObject, inside => view.Tip(heirloom, inside));
                view._heirlooms.Add(heirloom);
                at += step;
            }

            // The tooltip stands above the bar, in screen pixels, and over whatever lies on the bar.
            view._tipFrame = UiKit.Stretch(UiKit.Rect("EstateSummaryTips", canvasRoot));
            view._tooltip = new Dd1Tooltip(view._tipFrame, 0f);
            return view;
        }

        private static DarkestFile Load(string file) => Dd1Install.Found ? DarkestFile.Load(file) : null;

        /// <summary>The estate's heirlooms in the order DD1 lists its currencies (bust, portrait, deed, crest).</summary>
        private static List<string> HeirloomOrder()
        {
            var order = new List<string>();
            try
            {
                var text = Dd1Install.Found ? Dd1Install.ReadText(CurrencyTable) : null;
                if (text != null)
                    foreach (var entry in (JArray)JObject.Parse(text)["currencies"])
                    {
                        var id = (string)entry["id"];
                        if ((bool?)entry["is_heirloom"] == true && Array.IndexOf(EstateState.HeirloomIds, id) >= 0 && !order.Contains(id)) order.Add(id);
                    }
            }
            catch (Exception e) { Plugin.Log.LogWarning("DD1 currency table could not be read: " + e.Message); }
            if (order.Count == 0)
                foreach (var id in StockOrder)
                    if (Array.IndexOf(EstateState.HeirloomIds, id) >= 0) order.Add(id);
            // Whatever DD1 does not list keeps the estate's own order.
            foreach (var id in EstateState.HeirloomIds)
                if (!order.Contains(id)) order.Add(id);
            return order;
        }

        /// <summary>A count as DD1 writes one: "4,990".</summary>
        private static string Count(int amount) => amount.ToString("N0", CultureInfo.InvariantCulture);

        /// <summary>A count's width as DD1 measures it: its glyphs' advances (12 px a figure in the small font), as the label has laid them out.</summary>
        private static float Advance(TextMeshProUGUI label)
        {
            label.ForceMeshUpdate();
            var info = label.textInfo;
            return info != null && info.characterCount > 0
                ? info.characterInfo[info.characterCount - 1].xAdvance - info.characterInfo[0].origin
                : label.GetPreferredValues(label.text).x;
        }

        // Icon and count as one block with its middle on the currency's place.
        private void Seat(Heirloom heirloom)
        {
            var left = heirloom.Place.x - (_iconAt.x + _numberAt.x + Advance(heirloom.Amount)) * 0.5f;
            heirloom.Icon.anchoredPosition = new Vector2(left + _iconAt.x, -(heirloom.Place.y + _iconAt.y));
            ((RectTransform)heirloom.Amount.transform).anchoredPosition = new Vector2(left + _numberAt.x, -(heirloom.Place.y + _numberAt.y));
        }

        // The gold's count likewise; its icon is not drawn, so only the count moves.
        private void SeatGold()
        {
            _gold.fontSize = _goldSize;
            var width = Advance(_gold);
            if (width > _goldRoom)
            {
                _gold.fontSize = Mathf.Max(GoldSmallest, _goldSize * _goldRoom / width);
                width = Advance(_gold);
            }
            ((RectTransform)_gold.transform).anchoredPosition = new Vector2(_goldPlace.x + _goldAt.x - (_goldAt.x + width) * 0.5f, -(_goldPlace.y + _goldAt.y));
        }

        // localization: town_estate_bar_tooltip_<id> ("Busts: %d (Used to upgrade town buildings.)"). The box's corner
        // stands tooltip_offset from the currency's place (DD1's own screen with the pointer on the portraits, whose
        // place is 474, 1000: the box from 475, 952).
        private void Tip(Heirloom heirloom, bool show)
        {
            if (_tooltip == null) return;
            if (!show)
            {
                _tooltip.Hide(heirloom);
                return;
            }
            var format = Dd1Strings.Get("town_estate_bar_tooltip_" + heirloom.Id)
                         ?? char.ToUpperInvariant(heirloom.Id[0]) + heirloom.Id.Substring(1) + "s: %d (Used to upgrade town buildings.)";
            _tipFrame.SetAsLastSibling();
            _tooltip.ShowAt(heirloom, _origin + heirloom.Place + _tipAt, null, Dd1Strings.Format(format, EstateState.Current.Heirloom(heirloom.Id)), TipWidth);
        }

        private void OnEnable()
        {
            _nextRefresh = 0f;
            // what the bar shows when it comes back is where the watching starts: no flare for gold spent elsewhere
            _goldShown = -1;
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextRefresh || !EstateSession.Active) return;
            _nextRefresh = Time.unscaledTime + RefreshSeconds;
            var gold = _goldInstead ?? EstateState.Gold;
            if (gold != _goldShown)
            {
                // DD1 plays the pile's "spend" beside the sound of a purchase.
                if (_goldShown >= 0 && gold < _goldShown && _pile != null && _pile.Has(PileSpend)) _pile.Play(PileSpend, PileIdle);
                _goldShown = gold;
                _gold.text = Count(gold);
                SeatGold();
            }
            var state = EstateState.Current;
            foreach (var heirloom in _heirlooms)
            {
                var amount = state.Heirloom(heirloom.Id);
                if (amount == heirloom.Shown) continue;
                heirloom.Shown = amount;
                heirloom.Amount.text = Count(amount);
                Seat(heirloom);
            }
        }
    }
}
