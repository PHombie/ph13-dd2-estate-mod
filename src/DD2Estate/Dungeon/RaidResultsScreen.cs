using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DD2Estate.Core;
using DD2Estate.Dd1;
using DD2Estate.Dd2;
using DD2Estate.Estate;
using DD2Estate.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// DD1's two screens after an expedition (class RaidResultsUI in its exe; docs/recon/dd1-raid-results.md),
    /// drawn with the art of the player's DD1 install and laid out by its own files (<see cref="RaidResultsLayout"/>).
    /// They are a view of a <see cref="RaidResults"/> record: everything it tells has been done already.
    ///
    /// Both pages share the region's picture (its loading screen), the outcome's panel hanging from the top
    /// (a blue plate for a quest completed, the red sun for an escape, the sun and a crow's skull for a
    /// defeat), the result and the quest's name on it, and DD1's bar along the foot with the red button.
    ///
    /// - Page 1: "Quest Rewards" (what the quest offered; withheld, in DD1's grey, when it was not completed),
    ///   "Collected Treasure" (every stack of the bag that is worth gold, counted into the total one by one)
    ///   and "Collected Heirlooms" (the stacks, counted into the four totals).
    /// - Page 2: a row for every hero who set out. Name, portrait (DD1's skull over a dead hero's), the
    ///   experience gained sliding in while the bar under the badge fills, the stress in pips, and the
    ///   quirks and disease of the quest's end behind a mask until it is clicked.
    ///
    /// The button, as in DD1: while a page is still revealing itself a click finishes that and nothing more;
    /// then "Next" turns to page 2; on page 2 it first opens every mask left, then is "Return to Town".
    /// There is no way back to page 1.
    ///
    /// What the mod puts in DD1's places: DD2's portraits, trinkets and quirks; gold in the estate's own
    /// currency (a card's worth to one decimal, the total rounded as the estate was paid); stress as DD2 counts
    /// it, a pip a point. Canvas "DD2Estate.RaidResults", sort order 6: over the dungeon's (4) and the hamlet's (5).
    /// </summary>
    internal class RaidResultsScreen : MonoBehaviour
    {
        private const string Dir = RaidResultsLayout.Dir;
        private const string ProgressionDir = "shared/progression/";
        private const string LootFx = "raid_results_loot_glow", MaskFx = "raid_results_quirk_reveal";
        private const string Sounds = "general/raid_screen/";

        // DD1's screen and art sizes (pixels, y down).
        private const float ScreenHeight = 1080f;
        private static readonly Vector2 PanelArt = new Vector2(899f, 1080f);        // raid_results.quest_*_background.png
        private static readonly Vector2 ItemsArt = new Vector2(533f, 723f);         // raid_results.items_frames.png
        private static readonly Vector2 HeroesArt = new Vector2(680f, 675f);        // raid_results.heroes_frames.png
        private const float BarHeight = 138f;                                       // shared/progression/progression_bar.png (1920x138)
        private static readonly Vector2 ForwardSize = new Vector2(312f, 52f);       // progression_forward.png
        private static readonly Vector2 ForwardGlowSize = new Vector2(311f, 24f);   // progression_forward_selected_overlay.png
        private static readonly Vector2 Card = new Vector2(72f, 144f);              // panels/icons_equip/*/inv_*.png
        private static readonly Vector2 PileArt = new Vector2(88f, 88f);            // shared/estate/currency.gold.large_icon.png
        private static readonly Vector2 HeirloomIcon = new Vector2(40f, 40f);       // shared/estate/currency.<kind>.icon.png
        private const float PortraitSize = 85f;                                     // the hole of a row; deadhero_portrait.png
        private static readonly Vector2 DiseaseArt = new Vector2(24f, 24f);         // shared/hero/portrait_icon_disease.png
        private static readonly Vector2 PipEmpty = new Vector2(8f, 12f);            // overlays/stress_pip_empty.png
        private static readonly Vector2 PipFull = new Vector2(9f, 10f);             // overlays/stress_pip_full.png
        private static readonly Vector2 MaskArt = new Vector2(96f, 98f);            // the two halves of the mask side by side
        private const int Pips = 10;                                                // DD1 draws stress as ten pips

        // DD1 behaviour, not in data (constants of RaidResultsDisplay::InitCampaignAnimTweens): the second page
        // waits, then brings in the experience with a sound, then flashes the masks.
        private const float ExperienceAt = 0.661f, MaskFlashAt = 1.433f;

        // The mod's own numbers.
        private const float PortraitDrawn = 106f;           // DD2's portrait in DD1's 85 px hole, as the raid banner has it
        private const float TrinketTooltip = 320f;
        private const float PopupFadeFrom = 0.6f;           // a card's rising worth is whole this far up, then goes
        private const float BedFade = 1.5f;                 // the screen's music going as the hamlet's comes
        private const float ArtEvery = 0.2f;                // seconds between two looks for art the game hands out late
        private const float LineHeight = 24f;               // a quirk's line: new_quirks_from_quest_spacing
        private const float QuirkWidth = 270f;              // between the two halves of an opened mask, which rest at the plate's ends

        private enum MaskState { None, Waiting, Active, Opening, Open }

        /// <summary>Something timed of a page's reveal: done once when its moment comes, or at once when the reveal is cut short.</summary>
        private class Step
        {
            public float At;
            /// <summary>The argument says whether the reveal was cut short (no sound, no effect, no motion then).</summary>
            public Action<bool> Do;
        }

        /// <summary>Something that moves for a while. A reveal cut short puts it at its end.</summary>
        private class Tween
        {
            public float Start, Seconds;
            public Action<float> Apply;
            public Action End;
        }

        private class HeroRow
        {
            public RaidResults.Hero Hero;
            public Vector2 Origin;
            public ResolveBar Bar;
            public TextMeshProUGUI Experience;
            public Vector2 ExperienceAt;
            public SpineView Mask;
            public Image MaskHit;
            public MaskState State;
            /// <summary>The quest's experience has been brought in (the bar runs to it, or stands at it).</summary>
            public bool Brought;
            public readonly List<TextMeshProUGUI> Quirks = new List<TextMeshProUGUI>();
        }

        /// <summary>A trinket's icon the game has yet to load.</summary>
        private class IconWait
        {
            public Image Image;
            public string Trinket;
            public Color Tint;
        }

        private class Total
        {
            public string Kind;
            public RectTransform Root;
            public Image Icon;
            public TextMeshProUGUI Count;
            public int Amount;
        }

        private static RaidResultsScreen _instance;
        private static RaidResults _last;

        private RectTransform _root, _back, _front, _page;
        private Image _region;
        private RaidTooltip _tooltip;
        private TextMeshProUGUI _word, _goldCount;
        private RectTransform _goldGroup;
        private Image _goldPile;
        private RaidResultsLayout _l;
        private RaidResults _results;
        private Action _onReturn;
        private Dd1Playing _bed;
        private bool _open, _fromRun;
        private int _pageNumber;
        private float _pageTime;
        private int _goldCounted;
        private readonly List<Step> _steps = new List<Step>();
        private readonly List<Tween> _tweens = new List<Tween>();
        private readonly List<Total> _totals = new List<Total>();
        private readonly List<HeroRow> _rows = new List<HeroRow>();
        private readonly List<IconWait> _trinketIcons = new List<IconWait>();
        private readonly List<KeyValuePair<Image, RaidResults.Hero>> _portraits = new List<KeyValuePair<Image, RaidResults.Hero>>();
        private int _cardsShown, _cardsInAll;
        private float _nextArt;

        public static bool IsOpen => _instance != null && _instance._open;

        /// <summary>The screen's canvas while it is up: where the Ancestor's last word on the quest is shown (DD1 writes it over the bar here too).</summary>
        public static Transform Stage => IsOpen ? _instance.transform : null;

        /// <summary>The record of the last expedition that ended in this session; null before the first.</summary>
        public static RaidResults Last => _last;

        /// <summary>
        /// Puts the two screens up. <paramref name="onReturn"/> is called when the player leaves by "Return to
        /// Town" (null: the screens were opened to be looked at, and simply close). False when they could not
        /// be built: the caller goes on without them.
        /// </summary>
        public static bool Show(RaidResults results, Action onReturn)
        {
            if (results == null) return false;
            try
            {
                if (_instance == null)
                {
                    var canvas = UiKit.Canvas("DD2Estate.RaidResults", 6);
                    _instance = canvas.gameObject.AddComponent<RaidResultsScreen>();
                    _instance._root = (RectTransform)canvas.transform;
                }
                if (onReturn != null) _last = results;
                _instance.Open(results, onReturn);
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Raid results: the screens could not be built: " + e);
                if (_instance != null) _instance.Shut();
                return false;
            }
        }

        /// <summary>Takes the screens down without going anywhere (the session ended, or the bridge closed them).</summary>
        public static void Close()
        {
            if (_instance != null) _instance.Shut();
        }

        // ---- construction: rebuilt on every opening ---------------------------------------------------------

        private void Open(RaidResults results, Action onReturn)
        {
            _results = results;
            _onReturn = onReturn;
            _fromRun = onReturn != null;
            _l = RaidResultsLayout.Current;
            StopBed(0.2f);
            // everything of the last opening goes, but not the Ancestor's band if it was left standing here
            for (var i = _root.childCount - 1; i >= 0; i--)
            {
                var old = _root.GetChild(i).gameObject;
                if (old.GetComponent<NarrationBox>() != null) continue;
                old.SetActive(false);
                Destroy(old);
            }
            _nextArt = 0f;

            // What is under the screens takes no click meanwhile; a display of another shape shows black beside DD1's.
            var backdrop = UiKit.Image("Backdrop", _root, null, Color.black, true);
            ((RectTransform)backdrop.transform).Stretch();
            // DD1: the region's loading screen (loading_screen.<dungeon>_<n>.png; the base game has only _0), full screen
            var picture = RaidUi.Sprite("loading_screen/loading_screen." + results.Dungeon + "_0.png");
            _region = UiKit.Image("Region", _root, picture, picture != null ? Color.white : Color.clear);
            ((RectTransform)_region.transform).Stretch();

            _back = RaidUi.Screen("Screen", _root);
            BuildShared();
            _page = UiKit.Rect("Page", _back);
            _page.PlaceTopLeft(Vector2.zero, RaidUi.TopLeft, new Vector2(1920f, ScreenHeight));

            // The bar spans the display's foot whatever its width; its art is a little taller than what is left
            // of the screen under progression_bar_pos, and hangs below the edge as in DD1.
            var barArt = RaidUi.Sprite(ProgressionDir + "progression_bar.png");
            var bar = UiKit.Image("Bar", _root, barArt, barArt != null ? Color.white : new Color(0.02f, 0.016f, 0.016f, 0.95f), true);
            var barRect = (RectTransform)bar.transform;
            barRect.anchorMin = new Vector2(0f, 0f);
            barRect.anchorMax = new Vector2(1f, 0f);
            barRect.pivot = new Vector2(0.5f, 1f);
            barRect.offsetMin = new Vector2(0f, ScreenHeight - _l.Bar.y - BarHeight);
            barRect.offsetMax = new Vector2(0f, ScreenHeight - _l.Bar.y);

            _front = RaidUi.Screen("Front", _root);
            BuildButton();
            _tooltip = new RaidTooltip(_front);

            _open = true;
            gameObject.SetActive(true);
            ShowPage(1);
            // DD1's music table has a bed for either ending beside the town's and the expedition's
            _bed = Sound(results.Completed ? "success" : "fail");
            Plugin.Log.LogInfo("Raid results: " + results.Outcome + ", " + results.QuestName);
        }

        // The outcome's panel and the two titles on it.
        private void BuildShared()
        {
            string art, id, fallback;
            switch (_results.Outcome)
            {
                case RaidOutcome.Victory:
                    art = "raid_results.quest_completed_background.png";
                    id = "raid_results_quest_result_was_completed";
                    fallback = "Victory!";
                    break;
                case RaidOutcome.Escape:
                    art = "raid_results.quest_not_completed_escape_background.png";
                    id = "raid_results_quest_result_was_not_completed_escape";
                    fallback = "Escape";
                    break;
                default:
                    art = "raid_results.quest_not_completed_defeat_background.png";
                    id = "raid_results_quest_result_was_not_completed_defeat";
                    fallback = "Defeat";
                    break;
            }
            // completion_background_pos is the middle of the panel's top edge (DD1 draws it at the pixel below a half)
            RaidUi.Art("Panel", _back, Dir + art, new Vector2(Mathf.Floor(_l.Panel.x - PanelArt.x * 0.5f), _l.Panel.y), PanelArt, new Color(0f, 0f, 0f, 0.92f));

            // The titles stand on their positions: the middle of the foot of the line cell (the result in the sun's
            // disc or the blue plate, the quest's name in the band under it).
            var result = Standing("Result", "raid_results_quest_result", _l.QuestResult, 63f, Dd1Fonts.Colour(id, _results.Completed ? UiKit.Notable : UiKit.Harmful));
            result.text = RaidText.Get(id, fallback);
            var quest = Standing("Quest", "raid_results_quest_title", _l.QuestTitle, 40f, null);
            quest.text = _results.QuestName ?? "";
            RaidUi.Fit(quest, 16f);
        }

        // One centred line whose cell stands on a point. The cell's height is the font's own line; the stock
        // value (DD1's English fonts: 63 and 40) is for when the font cannot be had.
        private TextMeshProUGUI Standing(string name, string style, Vector2 foot, float stockLine, Color? colour)
        {
            var font = Dd1Fonts.Style(style);
            var line = font != null && font.LineHeight > 0 ? font.LineHeight : stockLine;
            return RaidUi.Label(name, _back, style, new Vector2(foot.x, foot.y - line), new Vector2(PanelArt.x - 120f, line + 4f), TextAlignmentOptions.Top, colour);
        }

        // DD1's progression button in the middle of the bar; its word changes with the page.
        private void BuildButton()
        {
            var forward = RaidUi.ArtButton("Forward", _front, ProgressionDir + "progression_forward.png", _l.Forward, Press, ForwardSize, lit: true);
            var glow = RaidUi.Art("Glow", forward.transform, ProgressionDir + "progression_forward_selected_overlay.png", _l.ForwardGlow, ForwardGlowSize);
            glow.gameObject.SetActive(false);
            UiKit.Hover(forward.gameObject, inside => glow.gameObject.SetActive(inside));
            _word = RaidUi.Label("Label", forward.transform, "town_progression_forward", _l.ForwardText, new Vector2(ForwardSize.x + 120f, 64f), TextAlignmentOptions.Top);
        }

        private void ShowPage(int page)
        {
            _pageNumber = page;
            _pageTime = 0f;
            _steps.Clear();
            _tweens.Clear();
            _totals.Clear();
            _rows.Clear();
            _trinketIcons.Clear();
            _portraits.Clear();
            _tooltip.HideAll();
            RaidUi.Clear(_page);
            if (page == 1) BuildItems();
            else BuildHeroes();
            _steps.Sort((a, b) => a.At.CompareTo(b.At));
            RefreshWord();
        }

        private void RefreshWord()
        {
            _word.text = _pageNumber == 1
                ? RaidText.Get("raid_results_progression_heroes", "Next")
                : RaidText.Get("raid_results_progression_return_to_town", "Return to Town");
        }

        // ---- page 1: rewards, treasure, heirlooms ---------------------------------------------------------------

        private void BuildItems()
        {
            var origin = _l.State;
            // frame_offset and the ribbon's words name the middle of a top edge; DD1 draws the frame at the pixel below a half
            RaidUi.Art("Frame", _page, Dir + "raid_results.items_frames.png", new Vector2(Mathf.Floor(origin.x + _l.ItemsFrame.x - ItemsArt.x * 0.5f), origin.y + _l.ItemsFrame.y), ItemsArt,
                new Color(0.02f, 0.02f, 0.02f, 0.9f));
            RaidUi.Label("RewardsTitle", _page, "raid_results_quest_inventory_title", origin + _l.RewardsTitle, new Vector2(300f, 42f), TextAlignmentOptions.Top).text =
                RaidText.Get("raid_results_quest_inventory_title", "Quest Rewards");
            RaidUi.Label("TreasureTitle", _page, "raid_results_party_gold_inventory_title", origin + _l.TreasureTitle, new Vector2(330f, 42f)).text =
                RaidText.Get("raid_results_party_gold_inventory_title", "Collected Treasure");
            RaidUi.Label("HeirloomTitle", _page, "raid_results_party_heirloom_inventory_title", origin + _l.HeirloomTitle, new Vector2(330f, 42f)).text =
                RaidText.Get("raid_results_party_heirloom_inventory_title", "Collected Heirlooms");

            BuildRewards(origin + _l.RewardsGrid + _l.Rewards.Start);
            BuildTotals(origin);

            // The bag's two rows: every card is there from the start and unseen until its turn. One after another,
            // treasure first, a card appears under DD1's glow, its worth rises from it and its total takes it in.
            var fx = UiKit.Rect("Fx", _page);
            fx.PlaceTopLeft(Vector2.zero, RaidUi.TopLeft, Vector2.zero);
            var at = _l.CardWait;
            _goldCounted = 0;
            _cardsShown = 0;
            _cardsInAll = _results.Treasure.Count + _results.Heirlooms.Count;
            at = BuildRow("Treasure", _results.Treasure, _l.Treasure, origin + _l.TreasureGrid + _l.Treasure.Start, fx, at, false);
            BuildRow("Heirloom", _results.Heirlooms, _l.Heirlooms, origin + _l.HeirloomGrid + _l.Heirlooms.Start, fx, at, true);
            fx.SetAsLastSibling();
            ShowGold();
        }

        // The quest's offer: gold, heirlooms, trinkets, a card each (DD1's reward list has no stack limit). Given
        // on a quest completed; otherwise every card wears DD1's look of what cannot be had (inventory_unselectable:
        // the card multiplied by #666, its amount in inventory_amount_unselectable).
        private void BuildRewards(Vector2 start)
        {
            var item = RaidLayout.Current;
            var cards = _results.Rewards;
            for (var i = 0; i < cards.Count; i++)
            {
                var reward = cards[i];
                RaidResultRules.GridSlot(i, cards.Count, _l.Rewards.Columns, _l.Rewards.Pitch.x, _l.Rewards.Pitch.y, _l.Rewards.Centred, false, out var x, out var y);
                var slot = start + new Vector2((float)x, (float)y);
                var tint = reward.Given ? Color.white : Dd1Fonts.Colour("inventory_unselectable", new Color32(0x66, 0x66, 0x66, 255));
                string art, amount = null, title, body;
                switch (reward.Kind)
                {
                    case QuestPayment.Gold:
                        // the picture of a purse of that size
                        var gold = InventoryContent.Items.Ensure(ItemTypes.Gold, "");
                        art = gold.IconPath(reward.Amount);
                        amount = reward.Amount.ToString("N0", CultureInfo.InvariantCulture);
                        title = InventoryText.Name(gold);
                        body = ItemWords(gold, false);
                        break;
                    case QuestPayment.Heirloom:
                        var heirloom = InventoryContent.Items.Ensure(ItemTypes.Heirloom, reward.Id);
                        art = heirloom.IconPath(reward.Amount);
                        amount = reward.Amount.ToString(CultureInfo.InvariantCulture);
                        title = InventoryText.Name(heirloom);
                        body = ItemWords(heirloom, false);
                        break;
                    default:
                        // DD1 draws a trinket on the card of its rarity; the trinket is DD2's, so its own icon sits on DD1's card
                        art = Trinkets.RarityArt(reward.Rarity);
                        title = reward.Id != null ? Trinkets.Name(reward.Id) : "Trinket";
                        body = QuestMapText.TrinketRarity(reward.Rarity) + (reward.Id != null ? "\n" + Trinkets.Description(reward.Id) : "");
                        break;
                }
                var card = RaidUi.Art("Reward" + i, _page, art, slot + item.ItemIconOffset, Card, new Color(0.08f, 0.07f, 0.06f, 0.95f), true);
                if (card.sprite != null) card.color = tint;
                if (reward.Kind == QuestPayment.Trinket && reward.Id != null)
                {
                    var icon = TrinketPicture.Add(card.transform);
                    // DD2 loads a trinket's icon on demand: the card asks until it has come
                    _trinketIcons.Add(new IconWait { Image = icon, Trinket = reward.Id, Tint = tint });
                }
                if (amount != null)
                {
                    // amount_text_offset counts from the slot; the card stands icon_offset inside it
                    var count = RaidUi.Label("Count", card.transform, "inventory_amount", item.ItemAmount - item.ItemIconOffset, new Vector2(Card.x + 30f, 40f), TextAlignmentOptions.TopLeft,
                        reward.Given ? Dd1Fonts.Colour("inventory_amount", UiKit.Notable) : Dd1Fonts.Colour("inventory_amount_unselectable", new Color32(0x5d, 0x5a, 0x50, 255)));
                    count.text = amount;
                }
                // (the last line is the mod's own: DD1 says it by the grey alone)
                var words = RaidTooltip.Titled(title, reward.Given ? body : body + (body.Length > 0 ? "\n" : "") + "<color=" + RaidText.Hex(UiKit.Harmful) + ">Withheld: the quest was not completed.</color>");
                var tipAt = slot + item.ItemTooltipOffset;
                // DD2's words for a trinket run longer than DD1's box for an item
                var tipWidth = reward.Kind == QuestPayment.Trinket ? TrinketTooltip : item.ItemTooltipWidth;
                RaidUi.Pointer(card, null, null, inside =>
                {
                    if (inside) _tooltip.Show(card, words, tipAt, tipWidth);
                    else _tooltip.Hide(card);
                });
            }
        }

        // The header of the treasure: the count and DD1's pile of gold as one group, centred on party_gold_total_offset
        // (the count first, the pile after it: 90 px for the pile, as estate_large_currency_layout spaces them).
        // The header of the heirlooms: an icon and a count for each of the four kinds, side by side, centred the same way.
        private void BuildTotals(Vector2 origin)
        {
            _goldGroup = UiKit.Rect("GoldTotal", _page);
            _goldGroup.PlaceTopLeft(origin + _l.TreasureTotal, RaidUi.Middle, Vector2.zero);
            _goldPile = RaidUi.Art("Pile", _goldGroup, "shared/estate/currency.gold.large_icon.png", Vector2.zero, PileArt, UiKit.Gold);
            // town_large_currency_amount has no colour of its own: DD1 draws it in town_currency_amount's
            _goldCount = RaidUi.Label("Count", _goldGroup, "town_large_currency_amount", Vector2.zero, new Vector2(400f, 66f), TextAlignmentOptions.TopLeft, Dd1Fonts.Colour("town_currency_amount", UiKit.Notable));

            var at = origin + _l.HeirloomTotal;
            foreach (var kind in _l.HeirloomOrder)
            {
                var total = new Total { Kind = kind, Root = UiKit.Rect("Total." + kind, _page) };
                total.Root.PlaceTopLeft(at, RaidUi.Middle, Vector2.zero);
                total.Icon = RaidUi.Art("Icon", total.Root, "shared/estate/currency." + kind + ".icon.png", Vector2.zero, HeirloomIcon, UiKit.Parchment);
                total.Count = RaidUi.Label("Count", total.Root, "town_currency_amount", Vector2.zero, new Vector2(80f, 28f));
                total.Count.text = "0";
                _totals.Add(total);
            }
            PlaceTotals();
        }

        private void ShowGold()
        {
            // the running sum: the last figure is what the estate was paid
            _goldCount.text = _goldCounted.ToString("N0", CultureInfo.InvariantCulture);
            var width = Mathf.Ceil(_goldCount.GetPreferredValues(_goldCount.text).x);
            var left = -(_l.LargeNumber.x + width) * 0.5f;
            ((RectTransform)_goldCount.transform).PlaceTopLeft(new Vector2(left, _l.LargeNumber.y), RaidUi.TopLeft, new Vector2(400f, 66f));
            ((RectTransform)_goldPile.transform).PlaceTopLeft(new Vector2(left + width, _l.LargeIcon.y), RaidUi.TopLeft, PileArt);
        }

        // An entry: the icon, the count number_offset from the icon's place, the next entry next_currency_spacing after the count.
        private void PlaceTotals()
        {
            var widths = new List<float>();
            var whole = 0f;
            foreach (var total in _totals)
            {
                var width = _l.SmallNumber.x + Mathf.Ceil(total.Count.GetPreferredValues(total.Count.text).x);
                widths.Add(width);
                whole += width;
            }
            whole += _l.SmallNext.x * Mathf.Max(0, _totals.Count - 1);
            var x = _l.State.x + _l.HeirloomTotal.x - whole * 0.5f;
            for (var i = 0; i < _totals.Count; i++)
            {
                var total = _totals[i];
                // the entry swells about its own middle
                total.Root.PlaceTopLeft(new Vector2(x + widths[i] * 0.5f, _l.State.y + _l.HeirloomTotal.y), RaidUi.Middle, Vector2.zero);
                ((RectTransform)total.Icon.transform).PlaceTopLeft(new Vector2(-widths[i] * 0.5f, 0f) + _l.SmallIcon, RaidUi.TopLeft, HeirloomIcon);
                ((RectTransform)total.Count.transform).PlaceTopLeft(new Vector2(-widths[i] * 0.5f, 0f) + _l.SmallNumber, RaidUi.TopLeft, new Vector2(80f, 28f));
                x += widths[i] + _l.SmallNext.x;
            }
        }

        // One of the bag's rows, in DD1's one-row grid (more cards than columns move closer together). The cards
        // show no count, as DD1's do not here: the totals and the worth that rises from a card say it.
        private float BuildRow(string name, List<HaulCard> cards, RaidResultsLayout.Grid grid, Vector2 start, RectTransform fx, float at, bool heirlooms)
        {
            var item = RaidLayout.Current;
            for (var i = 0; i < cards.Count; i++)
            {
                var entry = cards[i];
                RaidResultRules.GridSlot(i, cards.Count, grid.Columns, grid.Pitch.x, grid.Pitch.y, grid.Centred, true, out var x, out var y);
                var slot = start + new Vector2((float)x, (float)y);
                var corner = slot + item.ItemIconOffset;
                var card = RaidUi.Art(name + i, _page, entry.Item.IconPath(entry.Amount), corner, Card, new Color(0.25f, 0.2f, 0.12f), true);
                card.gameObject.SetActive(false);

                var title = entry.Item.Type == ItemTypes.Gold ? InventoryText.Amount(entry.Item, entry.Amount) + " " + InventoryText.Name(entry.Item)
                    : entry.Amount > 1 ? entry.Amount + " " + InventoryText.Name(entry.Item) : InventoryText.Name(entry.Item);
                var words = RaidTooltip.Titled(title, ItemWords(entry.Item, true));
                var tipAt = slot + item.ItemTooltipOffset;
                RaidUi.Pointer(card, null, null, inside =>
                {
                    if (inside) _tooltip.Show(card, words, tipAt, item.ItemTooltipWidth);
                    else _tooltip.Hide(card);
                });

                var middle = corner + Card * 0.5f;
                _steps.Add(new Step
                {
                    At = at,
                    Do = cut =>
                    {
                        card.gameObject.SetActive(true);
                        _cardsShown++;
                        if (heirlooms) CountHeirloom(entry, cut);
                        else CountTreasure(entry, middle, cut);
                        if (cut) return;
                        Sound(heirlooms ? "heirloom_add" : "treasure_add");
                        var glow = SpineView.Create("Glow", fx, SpineView.Fx(LootFx), middle);
                        if (glow != null) glow.Play(heirlooms ? "heirloom_glow" : "treasure_glow", false, () => Destroy(glow.gameObject));
                    }
                });
                at += _l.CardWait;
            }
            return at;
        }

        private void CountTreasure(HaulCard card, Vector2 middle, bool cut)
        {
            _goldCounted += card.Gold;
            ShowGold();
            if (cut) return;
            // DD1 (treasure_gold_total_scale_pulse): the total swells and settles
            Pulse(_goldGroup, _l.GoldPulse, _l.GoldPulseSeconds);
            // (treasure_item_value_popup_offset_y): the card's worth rises from it
            var popup = RaidUi.Label("Worth", _page, "raid_results_gold_popup", Vector2.zero, new Vector2(200f, 66f), TextAlignmentOptions.Top);
            popup.text = "+" + InventoryText.Number(card.Gold);
            var rect = (RectTransform)popup.transform;
            var from = new Vector2(middle.x, middle.y - 33f);
            _tweens.Add(new Tween
            {
                Start = _pageTime, Seconds = _l.PopupSeconds,
                Apply = t =>
                {
                    rect.PlaceTopLeft(from - new Vector2(0f, _l.PopupRise * t), RaidUi.TopCentre, new Vector2(200f, 66f));
                    popup.alpha = t < PopupFadeFrom ? 1f : 1f - (t - PopupFadeFrom) / (1f - PopupFadeFrom);
                },
                End = () => Destroy(popup.gameObject)
            });
        }

        private void CountHeirloom(HaulCard card, bool cut)
        {
            var total = _totals.Find(t => t.Kind == card.Item.Id);
            if (total == null) return;
            total.Amount += card.Amount;
            total.Count.text = total.Amount.ToString(CultureInfo.InvariantCulture);
            PlaceTotals();
            // (heirloom_total_scale_pulse)
            if (!cut) Pulse(total.Root, _l.HeirloomPulse, _l.HeirloomPulseSeconds);
        }

        // DD1's scale pulse: up to the value in the time given, and back in as long (easeInOutQuad).
        private void Pulse(RectTransform what, float scale, float seconds)
        {
            _tweens.Add(new Tween
            {
                Start = _pageTime, Seconds = seconds * 2f,
                Apply = t =>
                {
                    var s = 1f + (scale - 1f) * RaidResultsEase.Pulse(t);
                    if (what != null) what.localScale = new Vector3(s, s, 1f);
                }
            });
        }

        // DD1's own words for an item, and what one of it is worth brought home.
        private static string ItemWords(ItemDef item, bool worth)
        {
            var words = RaidText.Get("str_inventory_description_" + item.Type + item.Id, "") ?? "";
            if (worth && item.Type != ItemTypes.Gold && item.SellGold > 0)
            {
                var value = RaidText.Format(RaidText.Get("str_inventory_gold_value_format", "[Value: %s Gold Each]"), InventoryText.Number(item.SellGold));
                words += (words.Length > 0 ? "\n" : "") + "<color=" + RaidText.Hex(Dd1Fonts.Colour("inventory_tooltip_gold_value", UiKit.Notable)) + ">" + value + "</color>";
            }
            return words;
        }

        // ---- page 2: the heroes ---------------------------------------------------------------------------------

        private void BuildHeroes()
        {
            // the frame holds four rows and is drawn whole whatever the party's size
            RaidUi.Art("Frame", _page, Dir + "raid_results.heroes_frames.png", new Vector2(Mathf.Floor(_l.State.x + _l.HeroesFrame.x - HeroesArt.x * 0.5f), _l.State.y + _l.HeroesFrame.y), HeroesArt,
                new Color(0.02f, 0.02f, 0.02f, 0.9f));

            var heroes = _results.Heroes;
            for (var i = 0; i < heroes.Count && i < 4; i++) _rows.Add(BuildHero(heroes[i], _l.HeroOrigin(i)));

            // DD1 pays experience for a completed quest only; then the line and its sound are part of the page.
            var experience = _rows.Any(row => !row.Hero.Dead && row.Hero.Gained > 0);
            var masks = _rows.Any(row => row.State == MaskState.Waiting);
            var ready = experience ? ExperienceAt + Mathf.Max(_l.XpSlideSeconds, _l.XpBarSeconds) : MaskFlashAt;
            if (experience)
                _steps.Add(new Step
                {
                    At = ExperienceAt,
                    Do = cut =>
                    {
                        if (!cut) Sound("resolve_in");
                        foreach (var row in _rows) BringExperience(row, cut);
                    }
                });
            _steps.Add(new Step { At = MaskFlashAt, Do = cut => { if (!cut) Sound("mask_flash"); } });
            // while the experience is still coming in a mask cannot be opened (disabled_loop); after it, it can (active_loop)
            _steps.Add(new Step
            {
                At = Mathf.Max(ready, MaskFlashAt),
                Do = cut =>
                {
                    foreach (var row in _rows)
                    {
                        if (row.State != MaskState.Waiting) continue;
                        row.State = MaskState.Active;
                        row.Mask?.Play("active_loop", true);
                        if (row.MaskHit != null) row.MaskHit.raycastTarget = true;
                    }
                }
            });
            if (!masks) Plugin.Log.LogInfo("Raid results: nobody came home changed");
        }

        private HeroRow BuildHero(RaidResults.Hero hero, Vector2 origin)
        {
            var row = new HeroRow { Hero = hero, Origin = origin };
            var index = _rows.Count;

            var name = RaidUi.Label("Name" + index, _page, "raid_results_hero_name", origin + _l.HeroName, new Vector2(_l.XpText.x - 190f, 42f));
            name.text = hero.Name ?? "";
            RaidUi.Fit(name, 18f);

            // DD1's roster portrait fills the 85 px hole. DD2's is a head on clear ground with wide margins: drawn
            // larger and cut off at the hole's edge, as the raid banner does.
            var window = UiKit.Image("Portrait" + index, _page, null, Color.clear);
            ((RectTransform)window.transform).PlaceTopLeft(origin + _l.Portrait, RaidUi.TopLeft, new Vector2(PortraitSize, PortraitSize));
            window.gameObject.AddComponent<RectMask2D>();
            var portrait = UiKit.Image("Art", window.transform, null, Color.clear);
            portrait.preserveAspect = true;
            ((RectTransform)portrait.transform).Place(RaidUi.Middle, RaidUi.Middle, Vector2.zero, new Vector2(PortraitDrawn, PortraitDrawn));
            _portraits.Add(new KeyValuePair<Image, RaidResults.Hero>(portrait, hero));
            if (hero.Dead)
                // a dead hero keeps the row: name, the skull over the portrait, the badge; nothing else
                RaidUi.Art("Dead" + index, _page, Dir + "deadhero_portrait.png", origin + _l.DeadPortrait, new Vector2(PortraitSize, PortraitSize), new Color(0.25f, 0.03f, 0.02f, 0.85f));
            else if (hero.Diseased)
                RaidUi.Art("Disease" + index, _page, "shared/hero/portrait_icon_disease.png", origin + _l.Portrait + _l.DiseaseIcon, DiseaseArt);

            BuildStatus(row, index);
            if (hero.Dead) return row;

            // "+N Resolve XP" ends at resolve_from_quest_offset, on the name's line before the banner; unseen until it slides in
            if (hero.Gained > 0)
            {
                row.ExperienceAt = origin + _l.XpText;
                row.Experience = RaidUi.Label("Experience" + index, _page, "raid_results_hero_resolve_from_quest", row.ExperienceAt, new Vector2(260f, 27f), TextAlignmentOptions.TopRight);
                row.Experience.text = RaidText.Format(RaidText.Get("raid_results_hero_resolve_from_quest", "+%d Resolve XP"), hero.Gained);
                row.Experience.alpha = 0f;
            }

            if (hero.NewQuirks.Count == 0) return row;
            // The new quirks, a line each from the plate's middle; over them the mask until it is opened.
            var first = origin + _l.QuirksCentre + _l.QuirksStart;
            for (var j = 0; j < hero.NewQuirks.Count && j < RaidResultRules.QuirksPerHeroLimit; j++)
            {
                var gain = hero.NewQuirks[j];
                var colour = gain.Kind == QuestAftermath.Kind.Positive ? Dd1Fonts.Colour("quirk_positive", UiKit.Notable)
                    : gain.Kind == QuestAftermath.Kind.Disease ? Dd1Fonts.Colour("disease", UiKit.Harmful) : Dd1Fonts.Colour("quirk_negative", UiKit.Harmful);
                var at = first + _l.QuirksSpacing * j;
                var line = RaidUi.Label("Quirk" + index + "." + j, _page, "quirk_name", at, new Vector2(QuirkWidth, LineHeight + 3f), TextAlignmentOptions.Top, colour);
                line.text = gain.Name ?? gain.Id ?? "";
                RaidUi.Fit(line, 13f);
                line.alpha = 0f;
                var words = RaidTooltip.Titled(line.text, gain.Description);
                var tipAt = at + new Vector2(QuirkWidth * 0.5f + 12f, -8f);
                RaidUi.Pointer(line, null, null, inside =>
                {
                    if (inside && row.State == MaskState.Open) _tooltip.Show(line, words, tipAt, 320f);
                    else _tooltip.Hide(line);
                });
                line.raycastTarget = false;
                row.Quirks.Add(line);
            }
            // raid_results_heroes_masks.offset from the quirks' place: the plate's middle
            var maskAt = first + _l.Masks;
            row.Mask = SpineView.Create("Mask" + index, _page, SpineView.Fx(MaskFx), maskAt);
            row.State = MaskState.Waiting;
            row.Mask?.Play("disabled_loop", true);
            // the mask is a button of its own
            row.MaskHit = UiKit.Image("MaskHit" + index, _page, null, Color.clear);
            ((RectTransform)row.MaskHit.transform).PlaceTopLeft(maskAt, RaidUi.Middle, MaskArt);
            RaidUi.Pointer(row.MaskHit, () => OpenMask(row, true), null);
            row.MaskHit.raycastTarget = false;
            return row;
        }

        // DD1's "campaign status" widget on the black banner: the resolve badge with its bar, the stress in pips.
        // (Its affliction label stays empty: DD2 has no affliction that lasts.)
        private void BuildStatus(HeroRow row, int index)
        {
            var hero = row.Hero;
            var at = row.Origin + _l.Status;
            row.Bar = new ResolveBar(_page, at + _l.ResolveBar);
            row.Bar.Set(hero.XpBefore);
            for (var k = 0; k < Pips; k++)
            {
                var full = !hero.Dead && k < hero.Stress;
                var sprite = RaidUi.Sprite(full ? "overlays/stress_pip_full.png" : "overlays/stress_pip_empty.png");
                var pip = UiKit.Image("Pip" + index + "." + k, _page, sprite, sprite != null ? Color.white : full ? new Color(0.86f, 0.86f, 0.9f) : new Color(0.14f, 0.14f, 0.14f));
                // The lit pip stands on stress_bar_offset itself (a frame of DD1's page has it there to the pixel); the
                // socket's picture is a pixel taller at either end.
                ((RectTransform)pip.transform).PlaceTopLeft(at + _l.Stress + _l.StressSpacing * k - (full ? Vector2.zero : new Vector2(0f, 1f)), RaidUi.TopLeft, full ? PipFull : PipEmpty);
            }

            var hot = UiKit.Image("Status" + index, _page, null, Color.clear, true);
            ((RectTransform)hot.transform).PlaceTopLeft(at + _l.TooltipHot, RaidUi.TopLeft, _l.TooltipHotSize);
            var tipAt = at + _l.StatusTooltip;
            RaidUi.Pointer(hot, null, null, inside =>
            {
                if (!inside)
                {
                    _tooltip.Hide(hot);
                    return;
                }
                // what the bar shows: the experience of before until the quest's has come in
                var level = row.Bar.Level;
                var top = level >= Resolve.MaxLevel;
                var shown = row.Brought && !row.Bar.Running ? hero.XpAfter : hero.XpBefore;
                var words = RaidText.Format(RaidText.Get("resolve_bar_tooltip_level_line_format", "Level: %d"), level)
                            + "\n" + RaidText.Format(RaidText.Get("resolve_bar_tooltip_xp_line_format", "Resolve XP: %d/%d"), shown, top ? shown : Resolve.Threshold(level + 1));
                if (!hero.Dead)
                    words += "\n" + RaidText.Format(RaidText.Get("resolve_bar_tooltip_additional_stress_format", "{colour_start|stress}Stress:{colour_end} %d/%d"), hero.Stress, Pips);
                _tooltip.Show(hot, words, tipAt, _l.TooltipWidth);
            });
        }

        // The experience line slides in from the left of its place while it clears (raid_results_heroes_xptext_xoffset:
        // easeInOutElastic for the way, easeInOutQuad for the opacity) and the bar fills.
        private void BringExperience(HeroRow row, bool cut)
        {
            var hero = row.Hero;
            if (hero.Dead || hero.Gained <= 0) return;
            row.Brought = true;
            if (cut)
            {
                row.Bar.Set(hero.XpBefore);
                row.Bar.Animate(hero.XpBefore, hero.XpAfter, _l.XpBarSeconds);
                row.Bar.Finish();
                if (row.Experience != null)
                {
                    row.Experience.alpha = 1f;
                    ((RectTransform)row.Experience.transform).PlaceTopLeft(row.ExperienceAt + new Vector2(_l.XpSlideTo, 0f), RaidUi.TopRight, new Vector2(260f, 27f));
                }
                return;
            }
            row.Bar.Animate(hero.XpBefore, hero.XpAfter, _l.XpBarSeconds);
            if (row.Experience == null) return;
            var label = row.Experience;
            var rect = (RectTransform)label.transform;
            _tweens.Add(new Tween
            {
                Start = _pageTime, Seconds = _l.XpSlideSeconds,
                Apply = t =>
                {
                    rect.PlaceTopLeft(row.ExperienceAt + new Vector2(Mathf.LerpUnclamped(_l.XpSlideFrom, _l.XpSlideTo, RaidResultsEase.InOutElastic(t)), 0f), RaidUi.TopRight, new Vector2(260f, 27f));
                    label.alpha = RaidResultsEase.InOutQuad(t);
                }
            });
        }

        // A mask opens: DD1's "reveal" parts the two halves to the plate's ends, and the names come up between
        // them after raid_results_heroes_quirk_text_wait, over raid_results_heroes_quirk_text_alpha.
        private void OpenMask(HeroRow row, bool sound)
        {
            if (row.State != MaskState.Active) return;
            row.State = MaskState.Opening;
            if (row.MaskHit != null) row.MaskHit.raycastTarget = false;
            if (sound) Sound("quirk_click");
            if (row.Mask != null && row.Mask.Has("reveal")) row.Mask.Play("reveal", false);
            else if (row.Mask != null) row.Mask.gameObject.SetActive(false);
            _tweens.Add(new Tween
            {
                Start = _pageTime + _l.QuirkWait, Seconds = _l.QuirkFade,
                Apply = t =>
                {
                    foreach (var line in row.Quirks) line.alpha = Mathf.Clamp01(t);
                },
                End = () =>
                {
                    row.State = MaskState.Open;
                    foreach (var line in row.Quirks) line.raycastTarget = true;
                }
            });
        }

        // ---- the button -----------------------------------------------------------------------------------------

        private bool Revealing => _steps.Count > 0;

        /// <summary>DD1's ProgressForwardInternal. Returns what the click did, for the bridge.</summary>
        private string PressNow()
        {
            if (!_open) return "closed";
            // "Return to Town" has been pressed: DD1's loading screen is closing over the page
            if (LoadingScreen.IsUp) return "leaving";
            if (Revealing)
            {
                // the click finishes what the page was still showing, and nothing more
                FinishReveal();
                return "reveal finished";
            }
            if (_pageNumber == 1)
            {
                Sound("hero_click");
                ShowPage(2);
                return "page 2";
            }
            var closed = _rows.Where(row => row.State == MaskState.Active).ToList();
            if (closed.Count > 0)
            {
                // every mask left opens, with one sound; the way to town is the next click
                Sound("quirk_click");
                foreach (var row in closed) OpenMask(row, false);
                return "masks opened";
            }
            Sound("town_click");
            Return();
            return "returned to town";
        }

        private void Press() => PressNow();

        private void FinishReveal()
        {
            var steps = new List<Step>(_steps);
            _steps.Clear();
            foreach (var step in steps) step.Do(true);
            FinishTweens();
            foreach (var row in _rows) row.Bar?.Finish();
        }

        private void FinishTweens()
        {
            var tweens = new List<Tween>(_tweens);
            _tweens.Clear();
            foreach (var tween in tweens)
            {
                // a mask being opened is not part of the page's reveal: it runs on
                if (tween.Start > _pageTime)
                {
                    _tweens.Add(tween);
                    continue;
                }
                tween.Apply?.Invoke(1f);
                tween.End?.Invoke();
            }
        }

        private void Return()
        {
            var onReturn = _onReturn;
            // DD1: the town-visit loading screen stands between the results and the hamlet. The page stays until
            // black has closed over it; the hamlet is opened behind the picture. Screens opened to be looked at
            // (no way back to give) and an install without the picture close as before.
            if (onReturn != null && LoadingScreen.Show(LoadingScreen.Kind.Town, work: () => Leave(onReturn))) return;
            Leave(onReturn);
        }

        private void Leave(Action onReturn)
        {
            Shut();
            try { onReturn?.Invoke(); }
            catch (Exception e) { Plugin.Log.LogError("Raid results: the way back to town failed: " + e); }
        }

        private void Shut()
        {
            _open = false;
            _onReturn = null;
            StopBed(BedFade);
            _steps.Clear();
            _tweens.Clear();
            // the Ancestor's line goes with the screen it stood on (the hamlet shows its own)
            var band = GetComponentInChildren<NarrationBox>(true);
            if (band != null) NarrationBox.Hide();
            gameObject.SetActive(false);
        }

        // ---- time -----------------------------------------------------------------------------------------------

        private void Update()
        {
            if (!_open) return;
            // An expedition's screens belong to its session: gone to the main menu, they go too.
            if (_fromRun && !EstateSession.Active)
            {
                Shut();
                return;
            }
            // the screens' time is the player's: a paused or slowed game does not hold them up
            var delta = Time.unscaledDeltaTime;
            _pageTime += delta;
            while (_steps.Count > 0 && _steps[0].At <= _pageTime)
            {
                var step = _steps[0];
                _steps.RemoveAt(0);
                step.Do(false);
            }
            for (var i = _tweens.Count - 1; i >= 0; i--)
            {
                var tween = _tweens[i];
                if (_pageTime < tween.Start) continue;
                var t = Mathf.Clamp01((_pageTime - tween.Start) / Mathf.Max(0.0001f, tween.Seconds));
                tween.Apply?.Invoke(t);
                if (t < 1f) continue;
                _tweens.RemoveAt(i);
                tween.End?.Invoke();
            }
            foreach (var row in _rows) row.Bar?.Tick(delta);
            LoadArt();
        }

        // What the game hands out late: a trinket's icon (loaded on demand), a portrait it had dropped.
        private void LoadArt()
        {
            if (Time.unscaledTime < _nextArt) return;
            _nextArt = Time.unscaledTime + ArtEvery;
            for (var i = _trinketIcons.Count - 1; i >= 0; i--)
            {
                var wait = _trinketIcons[i];
                Sprite sprite = null;
                var given = wait.Image == null;
                try
                {
                    if (!given) sprite = Trinkets.Icon(wait.Trinket);
                    given |= sprite == null && Trinkets.HasNoIcon(wait.Trinket);
                }
                catch (Exception) { given = true; }
                if (sprite != null)
                {
                    wait.Image.sprite = sprite;
                    wait.Image.color = wait.Tint;
                    given = true;
                }
                if (given) _trinketIcons.RemoveAt(i);
            }
            foreach (var pair in _portraits)
            {
                var image = pair.Key;
                if (image == null || image.sprite != null) continue;
                Sprite sprite = null;
                try { sprite = HeroNames.Portrait(pair.Value.ClassId, pair.Value.Dead); }
                catch (Exception) { /* outside a session the game has no portraits to give */ }
                if (sprite == null) continue;
                image.sprite = sprite;
                image.color = pair.Value.Dead ? new Color(0.45f, 0.45f, 0.45f) : Color.white;
            }
        }

        // ---- sound ----------------------------------------------------------------------------------------------

        // The events of DD1's raid_screen bank. They are asked of the player itself: the estate's own audio has
        // nothing for the moment between the dungeon and the hamlet, and keeps the volumes either way.
        private static Dd1Playing Sound(string name)
        {
            if (!EstateAudio.Enabled) return null;
            try { return Dd1Audio.Play(Sounds + name); }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Raid results: " + name + " could not be played: " + e.Message);
                return null;
            }
        }

        private void StopBed(float fade)
        {
            try { _bed?.Stop(fade); }
            catch (Exception e) { Plugin.Log.LogWarning("Raid results: the music could not be stopped: " + e.Message); }
            _bed = null;
        }

        // ---- dev bridge -----------------------------------------------------------------------------------------

        /// <summary>The button, as a click on it.</summary>
        public static string DevPress() => IsOpen ? _instance.PressNow() : "the results screens are not open";

        /// <summary>A hero's mask, as a click on it (it opens only once the page has revealed itself).</summary>
        public static string DevMask(int hero)
        {
            if (!IsOpen || _instance._pageNumber != 2) return "the heroes' page is not open";
            if (hero < 0 || hero >= _instance._rows.Count) return "no such row";
            var row = _instance._rows[hero];
            if (row.State != MaskState.Active) return "the mask is " + row.State.ToString().ToLowerInvariant();
            _instance.OpenMask(row, true);
            return "opened";
        }

        public static object Describe()
        {
            if (!IsOpen) return new { open = false, last = _last?.Describe() };
            var s = _instance;
            return new
            {
                open = true,
                fromExpedition = s._fromRun,
                page = s._pageNumber,
                seconds = s._pageTime,
                revealing = s.Revealing,
                stepsLeft = s._steps.Count,
                button = s._word != null ? s._word.text : null,
                outcome = s._results.Outcome.ToString(),
                quest = s._results.QuestName,
                region = s._region != null && s._region.sprite != null ? s._region.sprite.name : null,
                music = s._bed?.Path,
                items = s._pageNumber != 1 ? null : new
                {
                    cardsShown = s._cardsShown, cards = s._cardsInAll,
                    gold = s._goldCount != null ? s._goldCount.text : null, goldCountedDd1 = s._goldCounted, goldInAllDd1 = s._results.TreasureGold,
                    heirlooms = s._totals.ToDictionary(t => t.Kind, t => t.Amount)
                },
                heroes = s._pageNumber != 2 ? null : s._rows.Select(row => new
                {
                    name = row.Hero.Name, dead = row.Hero.Dead, level = row.Bar.Level, bar = row.Bar.Share, levelGained = row.Bar.Gained, running = row.Bar.Running,
                    experience = row.Experience != null ? row.Experience.text : null, experienceShown = row.Experience != null ? row.Experience.alpha : 0f,
                    stress = row.Hero.Stress, mask = row.State.ToString(),
                    quirks = row.Quirks.Select(q => new { name = q.text, shown = q.alpha }).ToList()
                }).ToList(),
                record = s._results.Describe()
            };
        }
    }
}
