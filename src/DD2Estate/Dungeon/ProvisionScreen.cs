using System.Collections.Generic;
using System.Globalization;
using Assets.Code.Actor;
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
    /// DD1's provision screen, shown in the hamlet between the Estate Map and the expedition: the shop's
    /// shelves and the party's bag over the art of the player's DD1 install, laid out by DD1's own files.
    ///
    /// - campaign/town/town.layout.darkest (town_background_layout): the shop's backdrop and its keeper;
    ///   (town_screen_layout): the party's tray;
    /// - campaign/town/provision/provision.layout.darkest: the screen's icon and name, the two grids over
    ///   their own pictures, the line about selling back, the quest's dungeon and particulars;
    /// - shared/inventory/inventory.layout.darkest: a card in its cell, the stack's size, the price under it
    ///   (shared/estate/estate.layout.darkest: the coin and the number of a price);
    /// - campaign/town/embark_party/embark_party.layout.darkest: the four who go;
    /// - shared/progression/progression.layout.darkest: the red arrows back (to the Estate Map) and the way
    ///   forward on the bar, "Embark" again (as DD1's own screen has it).
    ///
    /// The estate's bar is the hamlet's own: the screen's pictures lie under it in the hamlet's canvas, so the
    /// gold, the heirlooms and the bar's buttons stay in sight as in DD1; the tray, the way forward, tooltips
    /// and DD1's questions are on a layer over it.
    ///
    /// A click on a shelf buys one, a click on the bag sells one back at the price paid (a right click moves
    /// five). Prices are DD1's own. DD1 takes the gold as things are bought; here the bill is paid on
    /// setting out, so while the shop is open the bar's gold shows what the purse will hold after it. Setting
    /// out with less food than DD1 advises asks DD1's question first.
    /// tools/preview_raid_hud.py draws the same layout offline: change one, change the other.
    /// </summary>
    internal class ProvisionScreen : MonoBehaviour
    {
        // DD1's screen and art sizes (reference pixels, y down).
        private const float ScreenWidth = 1920f, ScreenHeight = 1080f;
        private static readonly Vector2 ArtSize = new Vector2(1395f, 776f);         // provision.character_background.png
        private static readonly Vector2 TraySize = new Vector2(412f, 113f);          // embark_party.background.png
        private const float SlotSize = 85f;                                          // hero_slot.background.png
        private const int PartySlots = 4;
        private const float CoinSize = 24f;                                          // shared/estate/currency.gold.icon.png
        private const float PriceLine = 26f;                                         // a line of town_currency_amount (Ubuntu small)
        // FALLBACK: DD1's own post_dd1_chance .. post_dd4_chance of shared/dd_effects.darkest, provision_dd_flicker_animation.
        private static readonly float[] KeeperChances = { 0.01f, 0.05f, 0.08f, 0.10f };

        // The mod's own numbers.
        private const float PortraitInset = 3f, PortraitDrawn = 98f;                // DD2's portrait in DD1's slot, as in the roster
        private const int Several = 5;

        private const string Dir = "campaign/town/provision/";
        private const string ProgressionDir = "shared/progression/";
        private const string SlotDir = "campaign/town/hero_slot/";

        private static ProvisionScreen _instance;

        private bool _open, _embarkAway;
        private Transform _canvasRoot;
        private RectTransform _frame, _over, _overFrame;
        private Quest _quest;
        private ProvisionShop _shop;
        private InventoryGrid _shelves, _bag;
        private Vector2 _shelvesAt, _bagAt;
        private readonly List<TextMeshProUGUI> _prices = new List<TextMeshProUGUI>();
        private bool _goldTaken;
        private Button _forward;
        private RaidTooltip _tooltip;

        public static bool IsOpen => _instance != null && _instance._open;

        /// <summary>The quest the screen provisions for; null while it is shut.</summary>
        public static Quest Quest => IsOpen ? _instance._quest : null;

        public static void Open(Transform canvasRoot, Quest quest)
        {
            if (quest == null || canvasRoot == null) return;
            if (_instance == null)
            {
                var root = UiKit.Rect("ProvisionScreen", canvasRoot);
                UiKit.Stretch(root);
                _instance = root.gameObject.AddComponent<ProvisionScreen>();
                _instance._canvasRoot = canvasRoot;
                // takes every click meant for the hamlet underneath
                var backdrop = UiKit.Image("Backdrop", root, null, UiKit.Ink, true);
                UiKit.Stretch((RectTransform)backdrop.transform);
                var centre = new Vector2(0.5f, 0.5f);
                _instance._frame = UiKit.Rect("Frame", root).Place(centre, centre, Vector2.zero, new Vector2(ScreenWidth, ScreenHeight));
                // what stands on the estate's bar or over it: a layer of its own, over everything the hamlet draws
                _instance._over = UiKit.Rect("ProvisionScreenOver", canvasRoot);
                UiKit.Stretch(_instance._over);
                _instance._overFrame = UiKit.Rect("Frame", _instance._over).Place(centre, centre, Vector2.zero, new Vector2(ScreenWidth, ScreenHeight));
            }
            _instance.Show(quest);
        }

        public static void Close()
        {
            if (_instance == null) return;
            _instance._open = false;
            TownConfirm.Close(_instance);
            _instance.gameObject.SetActive(false);
            if (_instance._over != null) _instance._over.gameObject.SetActive(false);
            _instance.GiveGoldBack();
            if (_instance._embarkAway) TownChrome.ShowEmbark(_instance._canvasRoot, true);
            _instance._embarkAway = false;
        }

        private void Show(Quest quest)
        {
            _quest = quest;
            _shop = Provisioning.NewShop(quest.Length);
            // over the roster column (DD1's shop has none in sight), under the estate's bar and its buttons
            TownChrome.PutUnder(transform, "EstateSummary");
            _over.SetAsLastSibling();
            // this screen's way on stands where the hamlet's Embark does
            if (TownChrome.ShowEmbark(_canvasRoot, false)) _embarkAway = true;
            // active before it is filled: the prices measure themselves
            gameObject.SetActive(true);
            _over.gameObject.SetActive(true);
            _open = true;
            Build();
            TakeGold();
            Refresh();
        }

        // ---- construction: rebuilt on every opening (the stock and the party differ) ----------------------

        private void Build()
        {
            RaidUi.Clear(_frame);
            RaidUi.Clear(_overFrame);
            _prices.Clear();

            // DD1's own anchors; the stock values stand in if a file cannot be read.
            var found = Dd1Install.Found;
            var town = found ? Dd1.DarkestFile.Load("campaign/town/town.layout.darkest") : null;
            var layout = found ? Dd1.DarkestFile.Load(Dir + "provision.layout.darkest") : null;
            var currency = found ? Dd1.DarkestFile.Load("shared/estate/estate.layout.darkest") : null;
            var item = RaidLayout.Current;
            var artPos = HamletScreen.Offset(town, "town_background_layout", "area_pos", 144f, 132f);
            var characterPos = HamletScreen.Offset(town, "town_background_layout", "character_pos", 132f, 240f);
            var namePos = HamletScreen.Offset(layout, "provision_layout", "name_pos", 104f, 126f);
            var sellBackPos = HamletScreen.Offset(layout, "provision_layout", "provision_sell_back_info_pos", 1164f, 510f);
            var questPos = HamletScreen.Offset(layout, "provision_layout", "quest_info_pos", 1300f, 96f);
            var specsOffset = HamletScreen.Offset(layout, "provision_layout", "quest_specs_offset", 20f, -5f);
            var storePos = HamletScreen.Offset(layout, "provision_store_background_layout", "pos", 814f, 144f);
            var storeStart = HamletScreen.Offset(layout, "provision_store_grid_layout", "start_pos", 120f, 20f);
            var storePitch = HamletScreen.Offset(layout, "provision_store_grid_layout", "offset", 80f, 170f);
            var storeColumns = Columns(layout, "provision_store_grid_layout", 7);
            var partyPos = HamletScreen.Offset(layout, "provision_party_background_layout", "pos", 800f, 532f);
            var partyStart = HamletScreen.Offset(layout, "provision_party_grid_layout", "start_pos", 60f, 28f);
            var partyPitch = HamletScreen.Offset(layout, "provision_party_grid_layout", "offset", 80f, 160f);
            var partyColumns = Columns(layout, "provision_party_grid_layout", 8);
            var coinAt = HamletScreen.Offset(currency, "estate_currency_gold_layout", "icon_offset", 0f, -12f);
            var numberAt = HamletScreen.Offset(currency, "estate_currency_gold_layout", "number_offset", 25f, -14f);

            RaidUi.Art("Background", _frame, Dir + "provision.background.png", Vector2.zero, new Vector2(ScreenWidth, ScreenHeight));
            RaidUi.Art("Shop", _frame, Dir + "provision.character_background.png", artPos, ArtSize, new Color(0.05f, 0.05f, 0.055f, 0.97f));
            RaidUi.Art("Keeper", _frame, Dir + "provision.character.png", characterPos);
            // DD1: once the Darkest Dungeon has been entered the keeper shows another face for a moment, now and then
            DdFlicker.Attach(RaidUi.Art("KeeperDd", _frame, Dir + "provision.dd.character.png", characterPos), "provision_dd_flicker_animation",
                QuestBoard.Progress(QuestBoard.DarkestDungeon).Level, KeeperChances);

            // the two grids over their own pictures
            RaidUi.Art("ShelvesBack", _frame, Dir + "inventory_grid_background_store.png", storePos);
            var lines = _shop.Lines;
            _shelvesAt = storePos + storeStart;
            _shelves = InventoryGrid.Build("Shelves", _frame, _shelvesAt, Mathf.Max(1, lines.Count), storeColumns, storePitch);
            _shelves.Clicked = OnShelfClicked;
            _shelves.Hovered = slot => Hover(_shelves, _shelvesAt, slot, slot >= 0 && slot < _shop.Lines.Count ? _shop.Lines[slot].Item : null);
            for (var i = 0; i < lines.Count; i++)
            {
                // A price is DD1's currency widget at inventory_item_layout.cost_offset: the coin, then the
                // number (estate_currency_gold_layout), the offset being their middle line. GUESS: the two
                // together are centred on the offset, which lies under the card's middle; the file does not
                // say which point of the widget it is.
                var at = _shelvesAt + _shelves.SlotPosition(i) + item.ItemCost;
                var price = UiKit.Text("Price" + i, _frame, InventoryText.Number(lines[i].Price), "town_currency_amount", null, TextAlignmentOptions.TopLeft);
                price.textWrappingMode = TextWrappingModes.NoWrap;
                var left = at.x - (numberAt.x + Mathf.Ceil(price.GetPreferredValues(price.text).x)) * 0.5f;
                RaidUi.Art("Coin" + i, _frame, "shared/estate/currency.gold.icon.png", new Vector2(left + coinAt.x, at.y + coinAt.y), new Vector2(CoinSize, CoinSize));
                ((RectTransform)price.transform).PlaceTopLeft(new Vector2(left + numberAt.x, at.y + numberAt.y), RaidUi.TopLeft, new Vector2(storePitch.x, PriceLine));
                _prices.Add(price);
            }

            RaidUi.Art("BagBack", _frame, Dir + "inventory_grid_background_party.png", partyPos, null, new Color(0.03f, 0.03f, 0.035f, 0.9f));
            _bagAt = partyPos + partyStart;
            _bag = InventoryGrid.Build("Bag", _frame, _bagAt, _shop.Bag.SlotCount, partyColumns, partyPitch);
            _bag.Clicked = OnBagClicked;
            _bag.Hovered = slot => Hover(_bag, _bagAt, slot, _shop.Bag.Slot(slot)?.Item);

            // "[CLICK] inventory to sell back": its position is its middle, in the gap between the two grids
            var sellBack = UiKit.Text("SellBack", _frame, RaidText.Get("provision_sell_back_info", "[CLICK] inventory to sell back"), "provision_sell_back_info");
            sellBack.textWrappingMode = TextWrappingModes.NoWrap;
            ((RectTransform)sellBack.transform).PlaceTopLeft(sellBackPos, RaidUi.Middle, new Vector2(520f, 30f));

            // The quest: its dungeon, and after it what kind of quest it is, as measured on DD1's own screen
            // ("Ruins" from 997, 91; "Short | Apprentice (Lvl 1)" from 1064, 96 to 1300): the particulars END at
            // quest_info_pos, their line cell's top on it; the dungeon's name ends quest_specs_offset.x before
            // them, its cell that offset's y higher (the larger font's line sits 5 px up on the same foot).
            // "Short | Apprentice (Lvl 1)" has the difficulty in DD1's colour of that difficulty, as on the Estate Map.
            var specs = UiKit.Text("Specs", _frame, QuestMapText.Specifics(_quest), "provision_quest_info_specs", null, TextAlignmentOptions.TopRight);
            specs.richText = true;
            specs.textWrappingMode = TextWrappingModes.NoWrap;
            var specsWidth = Mathf.Ceil(specs.GetPreferredValues(specs.text).x);
            ((RectTransform)specs.transform).PlaceTopLeft(questPos, new Vector2(1f, 1f), new Vector2(specsWidth + 2f, 28f));
            var dungeon = UiKit.Text("Dungeon", _frame, RaidText.Get("dungeon_name_" + _quest.Dungeon, DungeonContent.DisplayName(_quest.Dungeon)),
                "provision_quest_info_dungeon_name", null, TextAlignmentOptions.TopRight);
            dungeon.textWrappingMode = TextWrappingModes.NoWrap;
            ((RectTransform)dungeon.transform).PlaceTopLeft(new Vector2(questPos.x - specsWidth - specsOffset.x, questPos.y + specsOffset.y), new Vector2(1f, 1f), new Vector2(420f, 44f));

            // DD1's scouting figure: "Scouting (25.0%)" from scouting_stat_pos, in the particulars' style (measured:
            // its cell's corner at 1380, 96; the style DD1's files name for it, scouting_text, is not what its
            // screen draws). The chance is the one the expedition starts with: DD1's base chance. What the
            // party's own trinkets and quirks would add to it is not counted here.
            var scoutingAt = HamletScreen.Offset(layout, "provision_layout", "scouting_stat_pos", 1380f, 96f);
            var chance = RaidRules.Load(new Dd1Files()).ScoutChanceBase * 100.0;
            var scouting = UiKit.Text("Scouting", _frame, RaidText.Get("str_scouting", "Scouting") + " (" + chance.ToString("0.0", CultureInfo.InvariantCulture) + "%)",
                "provision_quest_info_specs", null, TextAlignmentOptions.TopLeft);
            scouting.textWrappingMode = TextWrappingModes.NoWrap;
            ((RectTransform)scouting.transform).PlaceTopLeft(scoutingAt, RaidUi.TopLeft, new Vector2(240f, 28f));

            // DD1 keeps the estate's crest and the band with its name over this screen (the red arrows back stand
            // on the band, left of the name): the hamlet's own plate, copied in, since this screen lies over it.
            var plate = _canvasRoot != null ? _canvasRoot.Find("Title") : null;
            if (plate != null) Instantiate(plate.gameObject, _frame, false).name = "EstatePlate";

            // DD1's name of a town screen: the shop's icon and "Provision" (on DD1's own screen the icon lies at
            // 164, 152 and the name begins at 296: name_pos 104 126)
            Dd1ScreenName.Add(_frame, namePos, Dir + "provision.icon.png", RaidText.Get("town_name_provision", "Provision"), null,
                Mathf.Max(120f, storePos.x - Dd1ScreenName.NameLeft(namePos) - 10f));

            BuildBack(found);
            BuildTray(town, found);

            // The way forward on the estate's bar. DD1's own provision screen says "Embark" on it (a frame of it;
            // its string table also has "Set Off", town_progression_forward_set_off, which that screen does not
            // show).
            if (_forward != null) Destroy(_forward.gameObject);
            _forward = TownChrome.Forward(_over, RaidText.Get("town_progression_forward_embark", "Embark"), OnSetOut, out _);
            // under the tray, whose foot stands on it, and under the shade of a question
            _forward.transform.SetAsFirstSibling();

            _tooltip = new RaidTooltip(_overFrame);
        }

        // DD1's party tray over the bar: the four who go, as on the Estate Map.
        private void BuildTray(Dd1.DarkestFile town, bool found)
        {
            var party = found ? Dd1.DarkestFile.Load("campaign/town/embark_party/embark_party.layout.darkest") : null;
            var trayPos = HamletScreen.Offset(town, "town_screen_layout", "embark_party_pos", 754f, 871f);
            var background = HamletScreen.Offset(party, "embark_party_layout", "background_offset", -1f, 0f);
            var slotStart = HamletScreen.Offset(party, "embark_party_layout", "hero_slot_start_offset", 22f, 16f);
            var slotSpacing = HamletScreen.Offset(party, "embark_party_layout", "hero_slot_spacing", 93f, 0f);
            RaidUi.Art("Tray", _overFrame, "campaign/town/embark_party/embark_party.background.png", trayPos + background, TraySize, new Color(0.03f, 0.03f, 0.035f, 0.92f));

            var heroes = Provisioning.Party();
            for (var index = 0; index < PartySlots; index++)
            {
                // as on the estate map: four slots, rank 1 on the right; a party short of four leaves the left ones empty
                var at = trayPos + slotStart + slotSpacing * (PartySlots - 1 - index);
                var slot = RaidUi.Art("Slot" + index, _overFrame, SlotDir + "hero_slot.background.png", at, new Vector2(SlotSize, SlotSize), new Color(0.07f, 0.07f, 0.07f), true);
                if (index >= heroes.Count) continue;
                var window = UiKit.Rect("Window", slot.transform);
                window.PlaceTopLeft(new Vector2(PortraitInset, PortraitInset), RaidUi.TopLeft, new Vector2(SlotSize - 2f * PortraitInset, SlotSize - 2f * PortraitInset));
                window.gameObject.AddComponent<RectMask2D>();
                var sprite = HeroNames.Portrait(heroes[index]);
                var portrait = UiKit.Image("Portrait", window, sprite, sprite != null ? Color.white : Color.clear);
                portrait.preserveAspect = true;
                ((RectTransform)portrait.transform).Place(RaidUi.Middle, RaidUi.Middle, Vector2.zero, new Vector2(PortraitDrawn, PortraitDrawn));
            }
        }

        // DD1 leaves a screen by the red arrows at the head of the name plate's band, and by a right click: back to the Estate Map.
        private void BuildBack(bool found)
        {
            var progression = found ? Dd1.DarkestFile.Load(ProgressionDir + "progression.layout.darkest") : null;
            var backAt = HamletScreen.Offset(progression, "progression_layout", "back_pos", 228f, 82f);
            var arrows = RaidUi.Art("BackArrows", _frame, ProgressionDir + "progression_back.png", backAt, new Vector2(32f, 33f), UiKit.Blood);
            var hot = UiKit.Image("Back", _frame, null, Color.clear, true);
            ((RectTransform)hot.transform).PlaceTopLeft(backAt - new Vector2(6f, 6f), RaidUi.TopLeft, new Vector2(32f + 12f, 33f + 12f));
            RaidUi.Pointer(hot, OnBack, OnBack, inside => TownChrome.Highlight(arrows, inside));
            arrows.raycastTarget = false;
        }

        // ---- the gold on the estate's bar ----------------------------------------------------------------

        // The bar is the hamlet's and counts the purse as it is. While the shop is open it is told to show the
        // purse less the bill instead, the way DD1's gold goes down with every purchase; the bar writes and seats
        // that count by its own rule (the count and its icon one block, centred on the currency's place), and its
        // pile flares as the count drops.
        private void TakeGold()
        {
            _goldTaken = true;
            EstateSummary.ShowGold(_shop != null ? _shop.Purse - _shop.Cost : (int?)null);
        }

        private void GiveGoldBack()
        {
            if (_goldTaken) EstateSummary.ShowGold(null);
            _goldTaken = false;
        }

        // ---- state ---------------------------------------------------------------------------------------

        private void OnDisable()
        {
            // Still active itself: the hamlet was hidden around the screen. It must not come back with the hamlet.
            if (gameObject.activeSelf) _open = false;
        }

        private void Update()
        {
            if (!_open || !EstateSession.InHub || EstateSession.View != EstateSession.Screen.Hamlet) Close();
        }

        private void Refresh()
        {
            if (!_open || _shop == null) return;
            var lines = _shop.Lines;
            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                var affordable = _shop.CanBuy(line.Item) > 0;
                _shelves.Set(i, line.Item, line.Left, dim: !affordable);
                // DD1's red for a price the purse does not reach (not an empty shelf or a full bag)
                _prices[i].color = !affordable && line.Left > 0 && _shop.Bag.Room(line.Item) > 0
                    ? Dd1Fonts.Colour("town_currency_cant_afford_amount", UiKit.Harmful)
                    : Dd1Fonts.Colour("town_currency_amount", UiKit.Notable);
            }
            _bag.Show(_shop.Bag);
            if (_goldTaken) EstateSummary.ShowGold(_shop.Purse - _shop.Cost);
        }

        // DD1 hangs an item's tooltip beside its card (inventory_item_tooltip_layout): its name and DD1's line about it.
        private void Hover(InventoryGrid grid, Vector2 gridAt, int slot, ItemDef item)
        {
            if (item == null || slot < 0)
            {
                _tooltip.Hide(grid);
                return;
            }
            var l = RaidLayout.Current;
            var words = QuestMapText.Dd1String("str_inventory_description_" + item.Type + item.Id);
            _tooltip.Show(grid, RaidTooltip.Titled(InventoryText.Name(item), words), gridAt + grid.SlotPosition(slot) + l.ItemTooltipOffset, l.ItemTooltipWidth);
        }

        // ---- input ---------------------------------------------------------------------------------------

        private void OnShelfClicked(int slot, bool right)
        {
            if (TownConfirm.IsOpenFor(this) || slot < 0 || slot >= _shop.Lines.Count) return;
            Buy(_shop.Lines[slot].Item, right ? Several : 1);
        }

        private int Buy(ItemDef item, int amount)
        {
            var line = _shop.Line(item);
            var bought = line != null ? _shop.Buy(item, amount) : 0;
            if (bought > 0) EstateAudio.Ui("ui/town/buy");
            else if (line != null)
            {
                // DD1's sound of a click that does nothing; a full bag also gets DD1's word for it
                EstateAudio.Ui("ui/town/button_click_locked");
                if (line.Left > 0 && _shop.Bag.Room(item) == 0)
                    TownConfirm.Ask(this, RaidText.Get("not_enough_room_confirm_provisions", "Your inventory is full."), new[] { QuestMapText.Ok }, null);
            }
            Refresh();
            return bought;
        }

        private void OnBagClicked(int slot, bool right)
        {
            if (TownConfirm.IsOpenFor(this)) return;
            var stack = _shop.Bag.Slot(slot);
            if (stack == null) return;
            // What the party brought of its own stays in the bag: DD1's sound of a click that does nothing. (A sale
            // back has no sound of its own here: DD1's executable names /ui/town/sell with the Trinket Inventory
            // only, and /ui/town/buy with the gold on the estate's bar, which rings when gold is spent.)
            if (_shop.SellBackAt(slot, right ? Several : 1) == 0) EstateAudio.Ui("ui/town/button_click_locked");
            Refresh();
        }

        // The mod's own, for the dev bridge: the usual kit for this quest in one go (food, torches, a shovel).
        private void OnKit()
        {
            _shop.BuyStandardKit();
            Refresh();
        }

        // DD1 asks once when the party takes less food than the quest usually needs.
        private void OnSetOut()
        {
            // the party has set off already: DD1's loading screen is closing over this one
            if (TownConfirm.IsOpenFor(this) || LoadingScreen.IsUp) return;
            if (_shop.Food < _shop.MinimumFood)
            {
                var question = RaidText.Format(RaidText.Get("town_provision_not_enough_food_confirm_format",
                    "You haven't purchased much food for your expedition. It's recommended to take at least %d food for this quest. Still Embark?"), _shop.MinimumFood);
                TownConfirm.Ask(this, question, new[] { RaidText.Get("town_provision_embark_yes", "Yes"), RaidText.Get("town_provision_embark_no", "No") }, i => { if (i == 0) SetOut(); });
                return;
            }
            SetOut();
        }

        private void SetOut()
        {
            var quest = _quest;
            var bag = Provisioning.SetOut(_shop);
            // DD1's loading screen stands between the town and the dungeon; the expedition is made behind its
            // picture, and this screen stays until black has closed over it. Without the screen (no DD1 picture)
            // the party sets off as before.
            if (LoadingScreen.Show(LoadingScreen.Kind.Raid, quest.Dungeon, LoadingScreen.Dd1Quest(quest.Id), () => Embark(quest, bag))) return;
            Embark(quest, bag);
        }

        private void Embark(Quest quest, Core.Inventory bag)
        {
            Close();
            QuestBoard.Embark(quest, bag);
        }

        private void OnBack()
        {
            if (TownConfirm.IsOpenFor(this)) return;
            Close();
            QuestPanel.Open(_canvasRoot);
        }

        // ---- dev bridge ----------------------------------------------------------------------------------

        public static object Describe()
        {
            if (!IsOpen) return new { open = false };
            var shop = _instance._shop;
            var shelves = new List<object>();
            foreach (var line in shop.Lines)
                shelves.Add(new { item = line.Item.Key, name = InventoryText.Name(line.Item), left = line.Left, bought = line.Bought, price = line.Price, canBuy = shop.CanBuy(line.Item) });
            return new
            {
                open = true,
                quest = _instance._quest.Name,
                purse = shop.Purse,
                cost = shop.Cost,
                shown = EstateSummary.GoldShown,
                food = shop.Food,
                minimumFood = shop.MinimumFood,
                confirm = TownConfirm.IsOpenFor(_instance),
                shelves,
                bag = BagList(shop.Bag)
            };
        }

        public static List<object> BagList(Core.Inventory bag)
        {
            var items = new List<object>();
            for (var i = 0; bag != null && i < bag.SlotCount; i++)
            {
                var stack = bag.Slot(i);
                if (stack != null) items.Add(new { slot = i, item = stack.Item.Key, name = InventoryText.Name(stack.Item), amount = stack.Amount });
            }
            return items;
        }

        /// <summary>Stacks as the bridge tells them (the loot scroll's cards).</summary>
        public static List<object> StackList(IList<ItemStack> stacks)
        {
            var items = new List<object>();
            for (var i = 0; stacks != null && i < stacks.Count; i++)
                if (stacks[i] != null && stacks[i].Item != null) items.Add(new { index = i, item = stacks[i].Item.Key, name = InventoryText.Name(stacks[i].Item), amount = stacks[i].Amount });
            return items;
        }

        public static object DevBuy(string item, int amount)
        {
            if (!IsOpen) return "the provision screen is not open";
            var def = InventoryContent.Items.Find(item);
            return def == null ? (object)"no such item" : _instance.Buy(def, amount);
        }

        public static object DevSell(string item, int amount)
        {
            if (!IsOpen) return "the provision screen is not open";
            var def = InventoryContent.Items.Find(item);
            if (def == null) return "no such item";
            var sold = _instance._shop.SellBack(def, amount);
            _instance.Refresh();
            return sold;
        }

        public static bool DevPress(string button)
        {
            if (!IsOpen) return false;
            switch (button)
            {
                case "kit": _instance.OnKit(); return true;
                case "back":
                    TownConfirm.Close(_instance);
                    _instance.OnBack();
                    return true;
                case "setout":
                    // the bridge has answered DD1's question already
                    TownConfirm.Close(_instance);
                    _instance.SetOut();
                    return true;
                default: return false;
            }
        }

        private static int Columns(Dd1.DarkestFile layout, string block, int fallback)
        {
            var entry = layout?.Find(block);
            return entry != null && entry.Has("number_of_columns") ? Mathf.Max(1, entry.Int("number_of_columns", 0, fallback)) : fallback;
        }
    }
}
