using System;
using System.Collections.Generic;
using Assets.Code.Actor;
using Assets.Code.Game;
using Assets.Code.Library;
using Assets.Code.Roster;
using Assets.Code.Utils;
using DD2Estate.Core;
using DD2Estate.Dd1;
using DD2Estate.Estate;
using DD2Estate.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// The expedition's screen over the corridor, laid out as DD1's raid screen (scripts/layout/*.darkest of
    /// the player's DD1 install, <see cref="RaidLayout"/>). DD1 keeps the bottom third of its 1920x1080 screen
    /// for two 720 px panels between two pieces of side decor:
    ///
    /// - left, the selected hero: the banner (portrait, name, class, skills) over the hero panel (health,
    ///   stress, stats, gear, trinkets): <see cref="RaidHeroPanel"/>;
    /// - right, the map or the inventory, switched by the two tabs at the panel's right edge, with DD1's home
    ///   button over them: <see cref="Minimap"/>, <see cref="InventoryGrid"/>;
    /// - in the scene, under every hero, the health bar, the stress pips and the marks of what lasts on them, in
    ///   the look of DD2's fights (a copy of the fight's own panel, <see cref="Dd2Bars"/>) or in DD1's:
    ///   <see cref="RaidTrays"/>;
    /// - over the scene, what DD1 tells in passing: the numbers and words that pop up over a hero and the
    ///   announcement banner ("TRAP!", "Ambush!"): <see cref="RaidPopText"/>, <see cref="RaidAnnouncement"/>, fed
    ///   by <see cref="RaidFeedback"/>;
    /// - top centre the torch (the torch of DD2's fights, <see cref="Dd2Torch"/>, or DD1's gauge), top left the
    ///   quest's scroll with the retreat flag: <see cref="RaidTorch"/>, <see cref="RaidQuestInfo"/>;
    /// - at the right of the scene DD1's event scrolls for whatever the expedition asks: <see cref="RaidScrolls"/>.
    ///
    /// How it is played is DD1's as well: a click on a hero selects them, a click on an item of the inventory
    /// uses it (on the selected hero where a hero takes it; a trinket found on the way is put on them, and a
    /// click on a trinket they wear takes it off into the inventory: DD1 drags both ways, out of a fight), a
    /// shift-click throws a stack away, a click on a room of the map walks there; DD1's keys do what they do there (T a torch, TAB map or inventory, SPACE
    /// take all the loot, C the selected hero's sheet; W is the corridor's). The mod's own additions: the last lines of the expedition's log under the
    /// quest info (what DD1 says in a curio's own sentence and in the Ancestor's voice is still told there) and
    /// a line over the inventory for what the HUD has to say.
    /// tools/preview_raid_hud.py draws the same screen offline: change one, change the other.
    /// </summary>
    internal class DungeonHud : MonoBehaviour
    {
        private static DungeonHud _instance;

        private const float PanelHeight = 360f;             // DD1: 1080 - screen_guide.panel_top
        private static readonly Vector2 PanelArt = new Vector2(720f, 360f);     // panels/panel_map.png, panel_inventory.png
        private static readonly Vector2 DecorArt = new Vector2(250f, 360f);     // panels/side_decor.png
        private static readonly Vector2 TransitionArt = new Vector2(1920f, 20f);    // panels/panel_transition.png
        private static readonly Vector2 HomeArt = new Vector2(38f, 46f);        // panels/focuscam_button.png

        // The mod's own numbers (screen pixels).
        private static readonly Vector2 LogPos = new Vector2(24f, 196f);        // under the retreat flag
        private static readonly Vector2 LogSize = new Vector2(470f, 84f);
        private const int LogLines = 3;
        private const float LogSeconds = 9f, LogFade = 2f;
        private const float NoticeWidth = 640f;             // the line of user information wraps no wider than this
        private const float SkillsEvery = 2f;               // seconds between looks at the selected hero's skills

        /// <summary>
        /// The last lines of the expedition's log under the quest info: the mod's own element, which DD1 does not
        /// have (it tells these things in its banner, in pop text over the heroes and in the Ancestor's voice).
        /// Off; the dev bridge can put it back to read what the expedition says (hud.log).
        /// </summary>
        public static bool ShowLogLines;

        private RectTransform _screen;
        private RaidTooltip _tooltip;
        private RaidHeroPanel _hero;
        private RaidTrays _trays;
        private float _overlaysBack;
        private RaidPopText _pops;
        private RaidAnnouncement _announce;
        private RaidTorch _torch;
        private RaidQuestInfo _quest;
        private RaidScrolls _scrolls;
        private RaidConfirm _confirm;
        private Minimap _map;
        // DD1's party-order button and its picture as the file has it
        private Image _order;
        private Sprite _orderArt;
        private InventoryGrid _bag;
        private GameObject _mapSide, _bagSide;
        private bool _showBag;
        private TextMeshProUGUI _log, _noticeText;
        private Vector2 _noticeAt;
        private RaidBark _bark;
        // a card of the bag in the player's hand: the slot it came from (-1: none) and its picture at the pointer
        private int _carried = -1;
        private Image _ghost;

        private uint _selected;
        // "move" is chosen: the companions' places are marked, and the one clicked is the place the selected hero takes
        private bool _moving;
        private RaidMoveMarks _marks;
        private int _escapeFrame = -10;     // the frame Escape put "move" away
        private readonly List<uint> _party = new List<uint>();
        private List<RaidAbility> _skills;
        private uint _skillsOf;
        private float _skillsAt;
        private float _nextRefresh;
        private int _hover = -1;
        // the bag's trinket that found both slots of the hero taken: a click on one of the two changes places with it
        private int _held = -1;
        private string _notice;
        private float _noticeUntil;
        private string _logShown;
        private float _logAt;

        private RaidPrompt _prompt;
        private Action<int> _promptAnswer;
        private Action<int> _pickAnswer;
        private string _pickText;
        private IList<ItemStack> _loot;
        private Func<int, int> _lootTake;
        private Action _lootDone;
        private string _lootTitle;
        private DungeonRun _completeFor;
        private bool _completeTold;

        /// <summary>True while a question (or the loot scroll) is on screen.</summary>
        public static bool AskingExpedition => _instance != null && (_instance._promptAnswer != null || _instance._loot != null);

        /// <summary>
        /// True while anything of the screen waits for an answer: a question of the expedition or its loot scroll,
        /// DD1's confirm dialog (the retreat), the choice of a completed quest. The dungeon holds still under it:
        /// nobody walks, no door is used, nothing is turned to.
        /// </summary>
        public static bool Asking => AskingExpedition || (_instance != null && _instance.gameObject.activeSelf && (_instance._confirm.IsOpen || _instance._quest.ChoiceOpen));

        /// <summary>True while the loot scroll is on screen.</summary>
        public static bool LootOpen => _instance != null && _instance._loot != null;

        /// <summary>True while the player is asked to pick a stack of the bag.</summary>
        public static bool Picking => _instance != null && _instance._pickAnswer != null;

        /// <summary>The hero DD1's banner shows: the one items are used on.</summary>
        public static uint Selected => _instance != null ? _instance._selected : 0u;

        public static void ShowMap(Exploration exploration)
        {
            if (_instance == null) Show();
            _instance._map.Show(exploration);
        }

        /// <summary>
        /// Asks a question in one of DD1's scrolls; the callback gets the index of the chosen option. Which
        /// scroll, and which of DD1's buttons stands for which answer, is read off the expedition's state
        /// (<see cref="RaidPrompt.Infer"/>).
        /// </summary>
        public static void Ask(string text, string[] options, Action<int> answer)
        {
            Ask(RaidPrompt.Infer(DungeonRun.Current, text, options), answer);
        }

        /// <summary>Asks a question whose window and buttons the caller has chosen.</summary>
        public static void Ask(RaidPrompt prompt, Action<int> answer)
        {
            if (_instance == null) Show();
            _instance.OpenPrompt(prompt, answer);
        }

        /// <summary>
        /// Answers the open question (the dev bridge's way in, and a scroll's slot that was clicked). On the loot
        /// scroll 0 takes what fits and leaves the rest, 1 leaves it all.
        /// </summary>
        public static bool Answer(int option)
        {
            if (!AskingExpedition) return false;
            if (_instance._loot != null)
            {
                if (option == 0) _instance.TakeAllLoot();
                if (_instance._loot != null) _instance.CloseLoot();
                return true;
            }
            _instance.ClosePrompt(option);
            return true;
        }

        /// <summary>
        /// A stack of the bag is offered to the open scroll's slot (dragged onto it, or right-clicked in the
        /// inventory: DD1's "[RIGHT-CLICK] an inventory item to apply it to the object"). True when the scroll
        /// took it; the question is answered with <see cref="RaidPrompt.ItemAnswer"/> then.
        /// </summary>
        public static bool Offer(int bagSlot)
        {
            if (_instance == null || _instance._promptAnswer == null || _instance._prompt?.Offer == null) return false;
            if (!_instance._prompt.Offer(bagSlot))
            {
                _instance.Refresh();
                return false;
            }
            _instance.ClosePrompt(RaidPrompt.ItemAnswer);
            return true;
        }

        /// <summary>True while a scroll with a slot for an item of the bag is up.</summary>
        public static bool TakesItems => _instance != null && _instance._promptAnswer != null && _instance._prompt != null && _instance._prompt.TakesItems;

        /// <summary>
        /// DD1's line of user information: a few words in dark red over the pointer that go by themselves
        /// (user_information/user_information.darkest: user_information_popup .y_offset -60 .time 4.0; style
        /// user_information_popup, colour user_information_neutral). "You don't have a shovel!", "Not enough room!".
        /// </summary>
        public static void Inform(string text)
        {
            if (_instance != null && _instance.gameObject.activeSelf) _instance.Notice(text);
        }

        /// <summary>A hero of the party says a line in DD1's speech balloon (scrolls/bark_balloon.png, shared/ui.layout.darkest: bark_layout).</summary>
        public static void Bark(uint hero, string text)
        {
            if (_instance == null || !_instance.gameObject.activeSelf || string.IsNullOrEmpty(text)) return;
            if (!_instance._bark.Say(hero, text)) _instance.Notice(text);
        }

        public static string PromptText
        {
            get
            {
                if (_instance == null) return null;
                if (_instance._loot != null) return _instance._lootTitle;
                return _instance._promptAnswer != null ? _instance._prompt.Text : Picking ? _instance._pickText : null;
            }
        }

        public static string[] PromptOptions
        {
            get
            {
                if (_instance == null) return new string[0];
                if (_instance._loot != null) return new[] { RaidText.Get("str_overlay_loot_take_all", "Take All"), RaidText.Get("str_overlay_loot_close", "Close") };
                return _instance._promptAnswer != null ? _instance._prompt.Options : new string[0];
            }
        }

        /// <summary>Asks for a stack of the bag (to drop it). The callback gets the slot, or -1 when the player declines.</summary>
        public static void PickSlot(string text, Action<int> answer)
        {
            if (_instance == null) Show();
            _instance._pickText = text;
            _instance._pickAnswer = answer;
            _instance.ShowBag(true);
            _instance.Refresh();
        }

        /// <summary>Answers <see cref="PickSlot"/> (also the dev bridge's way in).</summary>
        public static bool Pick(int slot)
        {
            if (!Picking) return false;
            var answer = _instance._pickAnswer;
            _instance._pickAnswer = null;
            _instance._pickText = null;
            answer(slot);
            return true;
        }

        /// <summary>
        /// DD1's loot scroll. <paramref name="take"/> puts the stack of that index into the bag as far as it
        /// fits and returns what is left of it (the HUD writes that into the stack's amount);
        /// <paramref name="done"/> is called once, when the scroll is closed (by the player, or because
        /// nothing is left on it). What the stacks still hold then stays behind. While the scroll is open the
        /// bag can be made room in the DD1 way: a shift-click throws a stack away.
        /// </summary>
        public static void Loot(string title, string description, IList<ItemStack> stacks, Func<int, int> take, Action done)
        {
            if (_instance == null) Show();
            _instance.OpenLoot(title, description, stacks, take, done);
        }

        /// <summary>Forgets a question, a pick or a loot scroll nobody will answer any more (the expedition that asked is over).</summary>
        public static void Clear()
        {
            if (_instance == null) return;
            _instance._promptAnswer = null;
            _instance._prompt = null;
            _instance._pickAnswer = null;
            _instance._pickText = null;
            _instance._loot = null;
            _instance._lootTake = null;
            _instance._lootDone = null;
            _instance._notice = null;
            _instance._scrolls.Close();
            _instance._confirm.Close();
            _instance._quest.EndChoice();
            _instance._tooltip.HideAll();
            _instance._pops.Clear();
            _instance._bark.Clear();
            _instance._announce.Clear();
            _instance.LetGo();
        }

        // a card in the player's hand goes back where it came from, a skill that waited is put away
        private void LetGo()
        {
            if (_moving)
            {
                _moving = false;
                _skills = null;
                _marks?.Show(null);
            }
            _carried = -1;
            _wornCarried = -1;
            if (_bag != null) _bag.Carried = -1;
            if (_ghost != null && _ghost.gameObject.activeSelf) _ghost.gameObject.SetActive(false);
            _scrolls?.LightSlot(false);
        }

        // ---- DD1's feedback over the scene ---------------------------------------------------------------

        /// <summary>
        /// DD1's banner across the scene ("TRAP!", "Ambush!", "... is at Death's Door!"): at once, or after the
        /// banners asked for before it. Returns the seconds from now until it has been up for its time (0 when
        /// the screen is not up): what a caller waits that wants it read before it goes on.
        /// </summary>
        public static float Announce(RaidAnnounce what, string subject = null)
        {
            if (_instance == null || !_instance.gameObject.activeSelf || what == null) return 0f;
            return _instance._announce.Show(what, subject);
        }

        /// <summary>DD1's pop text over a hero of the party: the kind's own word, or the number or words given.</summary>
        public static bool Pop(uint hero, RaidPop kind, string text = null)
        {
            if (_instance == null || !_instance.gameObject.activeSelf) return false;
            return _instance._pops.Show(hero, kind, text);
        }

        /// <summary>True for a hero who has a tray on the screen: somebody to show something over.</summary>
        public static bool Shows(uint hero)
        {
            if (_instance == null || !_instance.gameObject.activeSelf) return false;
            foreach (var guid in _instance._trays.Shown)
                if (guid == hero) return true;
            return false;
        }

        /// <summary>Dev bridge: the banner, the pop texts and the trays as they are.</summary>
        internal static RaidAnnouncement Banner => _instance != null && _instance.gameObject.activeSelf ? _instance._announce : null;
        internal static RaidPopText PopTexts => _instance != null && _instance.gameObject.activeSelf ? _instance._pops : null;
        internal static RaidTrays Trays => _instance != null ? _instance._trays : null;
        internal static RaidTorch Torch => _instance != null ? _instance._torch : null;
        internal static Minimap Map => _instance != null ? _instance._map : null;
        internal static RaidBark Barks => _instance != null ? _instance._bark : null;
        internal static RaidHeroPanel HeroPanel => _instance != null ? _instance._hero : null;

        /// <summary>Dev bridge: the party as the screen has it, in marching order (the hero of rank n is n - 1).</summary>
        internal static IReadOnlyList<uint> Party => _instance != null ? _instance._party : null;

        public static void Show()
        {
            if (_instance == null)
            {
                var canvas = UiKit.Canvas("DD2Estate.DungeonHud", 4);
                _instance = canvas.gameObject.AddComponent<DungeonHud>();
                _instance.Build();
            }
            _instance.gameObject.SetActive(true);
            _instance._party.Clear();       // the trays are built anew: heroes may have died in the fight just over
            _instance._hero.Invalidate();
            _instance._skills = null;
            _instance.Refresh();
        }

        public static void Hide()
        {
            if (_instance == null) return;
            _instance._tooltip.HideAll();
            // nothing moves while the screen is away: what was in the air would hang there when it comes back
            _instance._pops.Clear();
            _instance._bark.Clear();
            _instance._announce.Clear();
            _instance.LetGo();
            _instance.gameObject.SetActive(false);
        }

        /// <summary>Selects a hero of the party (a click on them does the same).</summary>
        public static bool Select(uint guid)
        {
            if (_instance == null) return false;
            _instance.OnHero(guid, false);
            return _instance._selected == guid;
        }

        /// <summary>Shows the inventory (true) or the map (false) in the right panel, as DD1's two tabs do.</summary>
        public static void ShowInventory(bool inventory)
        {
            if (_instance != null) _instance.ShowBag(inventory);
        }

        // ---- construction ------------------------------------------------------------------------------

        private void Build()
        {
            var l = RaidLayout.Current;
            // DD1's scene ends at panel_top; whatever a wider or taller display shows under it is black.
            var floor = UiKit.Image("Floor", transform, null, Color.black, true);
            var floorRect = (RectTransform)floor.transform;
            floorRect.anchorMin = new Vector2(0f, 0f);
            floorRect.anchorMax = new Vector2(1f, 0f);
            floorRect.pivot = new Vector2(0.5f, 0f);
            floorRect.offsetMin = Vector2.zero;
            floorRect.offsetMax = new Vector2(0f, PanelHeight);
            RaidUi.Pointer(floor, null, PutAway);

            _screen = RaidUi.Screen("Screen", transform);

            // the two pieces of side decor fill what the panels leave: 0..safe_left and safe_right..1920
            RaidUi.Art("DecorLeft", _screen, "panels/side_decor.png", new Vector2(0f, l.PanelTop), DecorArt);
            // the same picture, mirrored: turned over about its left edge, which therefore stands at the screen's right edge
            var right = RaidUi.Art("DecorRight", _screen, "panels/side_decor.png", new Vector2(1920f, l.PanelTop), DecorArt);
            right.transform.localScale = new Vector3(-1f, 1f, 1f);

            BuildRightPanel(l);
            _tooltip = new RaidTooltip(_screen);
            _hero = new RaidHeroPanel(_screen, _tooltip, ShowSheet, OnWornTrinket)
            {
                // DD1: a worn trinket is carried to the hero's other slot or into the inventory
                CanCarryTrinket = CanCarryWorn, TrinketCarryBegan = OnWornDragBegan, TrinketCarryMoved = GhostTo, TrinketCarryEnded = OnWornDragEnded
            };

            // the seam of scene and panels, across the whole display
            var seam = RaidUi.Art("Transition", _screen, "panels/panel_transition.png", l.Transition, TransitionArt);
            var seamRect = (RectTransform)seam.transform;
            seamRect.offsetMin = new Vector2(seamRect.offsetMin.x - 2000f, seamRect.offsetMin.y);
            seamRect.offsetMax = new Vector2(seamRect.offsetMax.x + 2000f, seamRect.offsetMax.y);

            _trays = new RaidTrays(_screen, _tooltip)
            {
                Clicked = OnHero,
                // a hero carried onto another's place takes that rank
                CanCarry = () => DungeonRun.Current != null && DungeonRun.Current.CanReorder && !Asking && !Picking && !CampScreen.IsOpen && !_confirm.IsOpen,
                Carried = (hero, onto) =>
                {
                    if (DungeonRun.Current != null && DungeonRun.Current.MoveHero(hero, onto)) Refresh();
                }
            };
            // the places "move" can take the selected hero to, over the bars
            _marks = new RaidMoveMarks(_screen, _trays) { Clicked = guid => OnHero(guid, false), Cancelled = PutAway };
            _torch = new RaidTorch(_screen, _tooltip) { Reduce = snuff => DungeonRun.Current?.ReduceTorch(snuff) };
            _quest = new RaidQuestInfo(_screen, _tooltip, this)
            {
                AskRetreat = AskRetreat,
                Leave = () => DungeonRun.Current?.Leave(),
                // DD1's banner over the choice of a completed quest
                Banner = up =>
                {
                    if (up) _announce.Hold(RaidAnnounce.QuestComplete);
                    else _announce.Release(RaidAnnounce.QuestComplete.Key);
                }
            };

            _log = RaidUi.Paragraph("Log", _screen, "raid_quest_info_goals", LogPos, LogSize, TextAlignmentOptions.TopLeft, UiKit.Neutral, 13f);
            _log.richText = true;

            // DD1's feedback over the scene: what pops up over a hero, what a hero says, and the banner (it comes to the front when it shows)
            _pops = new RaidPopText(_screen, _trays);
            _bark = new RaidBark(_screen, _trays);
            _announce = new RaidAnnouncement(_screen);

            _scrolls = new RaidScrolls(_screen, _tooltip) { Answered = ClosePrompt };
            _confirm = new RaidConfirm(_screen, this);

            // DD1's line of user information, over the pointer (user_information_popup)
            _noticeText = UiKit.Text("UserInformation", _screen, "", Dd1Fonts.FontOf("user_information_popup") != null ? "user_information_popup" : "tooltip",
                Dd1Fonts.Colour("user_information_neutral", new Color32(143, 8, 0, 255)), TextAlignmentOptions.Top);
            _noticeText.raycastTarget = false;
            _noticeText.textWrappingMode = TextWrappingModes.Normal;
            _noticeText.gameObject.SetActive(false);

            // a card of the bag in the player's hand
            _ghost = UiKit.Image("Carried", _screen, null);
            _ghost.raycastTarget = false;
            ((RectTransform)_ghost.transform).PlaceTopLeft(Vector2.zero, RaidUi.Middle, l.ItemIconSize);
            _ghost.gameObject.SetActive(false);
            ShowBag(false);
        }

        // The right panel: DD1 draws the map and the inventory as two whole panel pictures (each shows its own
        // tab lit) and swaps them.
        private void BuildRightPanel(RaidLayout l)
        {
            var at = l.RightPanel;
            var map = RaidUi.Art("MapPanel", _screen, "panels/panel_map.png", at, PanelArt, new Color(0.03f, 0.03f, 0.035f), true);
            RaidUi.Pointer(map, null, PutAway);
            _mapSide = map.gameObject;
            _map = Minimap.Build(map.transform);
            _map.Hint = (room, text, icon) =>
            {
                if (text == null || icon == null) _tooltip.Hide(_map);
                else
                {
                    // beside the icon's upper right: three eighths of the icon as it is drawn from its middle (12 px for a
                    // room at DD1's scale, as much less for a hallway tile)
                    var drawn = icon.rect.width * icon.lossyScale.x / Mathf.Max(0.0001f, _screen.lossyScale.x);
                    var reach = drawn * 0.375f;
                    _tooltip.Show(_map, text, PointOf(icon) + new Vector2(reach, -reach), 260f, above: true);
                }
            };

            var bag = RaidUi.Art("InventoryPanel", _screen, "panels/panel_inventory.png", at, PanelArt, new Color(0.03f, 0.03f, 0.035f), true);
            RaidUi.Pointer(bag, null, PutAway);
            _bagSide = bag.gameObject;
            _bag = InventoryGrid.Build("Bag", bag.transform, l.BagStart, InventoryContent.NewBag().SlotCount, l.BagColumns, l.BagOffset);
            _bag.Clicked = OnBagClicked;
            _bag.Hovered = OnBagHovered;
            // DD1: a card is picked up with the left button and carried to another cell, to a curio's slot, to a hero
            _bag.Draggable = true;
            _bag.DragBegan = OnBagDragBegan;
            _bag.DragMoved = OnBagDragMoved;
            _bag.DragEnded = OnBagDragEnded;

            // tab_placement gives the lower tab's corner and one tab's size; the map's tab is the one above it
            Tab("MapTab", at + l.TabPos - new Vector2(0f, l.TabSize.y), l.TabSize, false);
            Tab("InventoryTab", at + l.TabPos, l.TabSize, true);
            var home = RaidUi.ArtButton("Home", _screen, "panels/focuscam_button.png", at + l.HomeButton, () =>
            {
                ShowBag(false);
                _map.CentreOnParty();
            }, HomeArt);
            // home_button_layout.tooltip_offset (1206 28) and reorder_party_layout.tooltip_offset (1206 90) are
            // counted from the panels' left corner: both tooltips begin left of their button and level with it
            // (tooltip_width 200), and end where the button begins.
            RaidUi.Pointer(home.targetGraphic, null, null, inside =>
            {
                if (inside) _tooltip.Show(home, RaidText.Get("str_centre_map_tooltip", "Center map on party"), l.LeftPanel + l.HomeTooltip, l.SideButtonTooltipWidth);
                else _tooltip.Hide(home);
            });

            // DD1's party-order button under it (panels/party_order_button.png, 38x46; panel.tab.darkest:
            // reorder_party_layout; at (1638, 810) in DD1's own frames): "Default Party order". It puts the heroes
            // back in the ranks they set out in. While they stand in them it is drawn with a fifth of its colour
            // (reorder_party_layout.desat_level 0.2), and in full once a hero has been moved.
            var order = RaidUi.ArtButton("PartyOrder", _screen, "panels/party_order_button.png", at + l.PartyOrderButton, () =>
            {
                var run = DungeonRun.Current;
                if (run != null && !Asking && !Picking && !CampScreen.IsOpen && run.RestoreOrder()) Refresh();
            }, HomeArt);
            _order = (Image)order.targetGraphic;
            _orderArt = _order.sprite;
            RaidUi.Pointer(_order, null, null, inside =>
            {
                if (inside) _tooltip.Show(order, RaidText.Get("str_party_default_order", "Default Party order"), l.LeftPanel + l.PartyOrderTooltip, l.SideButtonTooltipWidth);
                else _tooltip.Hide(order);
            });
        }

        // The party-order button as DD1 draws it: lit while there is an order to put back.
        private void ShowOrderButton(DungeonRun run)
        {
            if (_order == null || _orderArt == null) return;
            var changed = run != null && run.OrderChanged;
            var art = changed ? _orderArt : RaidUi.Desaturated(_orderArt, RaidLayout.Current.PartyOrderDesaturation) ?? _orderArt;
            if (_order.sprite != art) _order.sprite = art;
        }

        // A tab of the right panel: the panel's own picture shows which is lit. DD1 has no words for the two (no
        // string of its tables names them), so the pointer is told nothing.
        private void Tab(string name, Vector2 at, Vector2 size, bool inventory)
        {
            var tab = UiKit.Image(name, _screen, null, Color.clear, true);
            ((RectTransform)tab.transform).PlaceTopLeft(at, RaidUi.TopLeft, size);
            RaidUi.Pointer(tab, () => ShowBag(inventory));
        }

        private void ShowBag(bool inventory)
        {
            _showBag = inventory;
            if (_mapSide.activeSelf == inventory) _mapSide.SetActive(!inventory);
            if (_bagSide.activeSelf != inventory) _bagSide.SetActive(inventory);
        }

        // Where something drawn on the screen is, in DD1's pixels (y down from the top).
        private Vector2 PointOf(RectTransform rect)
        {
            var local = _screen.InverseTransformPoint(rect.position);
            return new Vector2(local.x + 960f, 1080f - local.y);
        }

        // ---- questions ---------------------------------------------------------------------------------

        private void OpenPrompt(RaidPrompt prompt, Action<int> answer)
        {
            _quest.EndChoice();
            _confirm.Close();
            _prompt = prompt;
            _promptAnswer = answer;
            _scrolls.Show(prompt);
            // a scroll with a slot for an item: DD1 turns the right panel to the inventory (seen in its frames of a curio's and an obstacle's window)
            if (prompt.Kind == RaidPromptKind.Curio || prompt.Kind == RaidPromptKind.Obstacle) ShowBag(true);
            Refresh();
        }

        private void ClosePrompt(int option)
        {
            var answer = _promptAnswer;
            _promptAnswer = null;
            _prompt = null;
            _scrolls.Close();
            answer?.Invoke(option);
        }

        private void AskRetreat()
        {
            var run = DungeonRun.Current;
            if (run == null || run.Busy || !run.Exploration.CanLeave || Asking || Picking) return;
            // DD1 has a question of its own for a quest that costs a hero to abandon (the Darkest Dungeon)
            var question = CostsAHero(run)
                ? RaidText.Get("retreat_raid_party_kill_darkestdungeon_confirm_question", "A random hero must give their life to ensure the others will escape. Really abandon quest?")
                : RaidText.Get("retreat_confirm_raid_question", "Are you sure you want to retreat? The heroes will suffer the stress of defeat...");
            string yes = RaidText.Get("retreat_confirm_raid_answer_yes", "Yes"), no = RaidText.Get("retreat_confirm_raid_answer_no", "No");
            // DD1 words that question by the dungeon (retreat_raid_party_kill_<dungeon>_...: the hamlet's streets have their own, "Flee" / "Stand Fast")
            if (CostsAHero(run) && run.Exploration.Map.DungeonId != QuestBoard.DarkestDungeon)
            {
                var own = PlotQuests.RetreatQuestion(run.Exploration.Map.DungeonId, out var ownYes, out var ownNo);
                if (own != null)
                {
                    question = own;
                    yes = ownYes ?? yes;
                    no = ownNo ?? no;
                }
            }
            // the dialog lies on a picture of the screen as it stands: without the flag's own tooltip, a skill that waits, a card in hand
            _tooltip.HideAll();
            LetGo();
            _confirm.Ask(question, new[] { yes, no }, i => { if (i == 0) DungeonRun.Current?.Leave(); });
        }

        // The expedition keeps its quest to itself; the week's offers know it by name.
        private static bool CostsAHero(DungeonRun run)
        {
            try
            {
                foreach (var quest in QuestBoard.Current())
                    if (quest.Name == run.Title) return quest.RetreatDeaths > 0;
            }
            catch (Exception e) { Plugin.Log.LogWarning("Dungeon HUD: the quest's terms of retreat are not known: " + e.Message); }
            return false;
        }

        // ---- loot --------------------------------------------------------------------------------------

        private void OpenLoot(string title, string description, IList<ItemStack> stacks, Func<int, int> take, Action done)
        {
            _quest.EndChoice();
            _loot = stacks;
            _lootTake = take;
            _lootDone = done;
            _lootTitle = title;
            _scrolls.ShowLoot(title, description, (IReadOnlyList<ItemStack>)new List<ItemStack>(stacks), TakeLoot, TakeAllLoot, CloseLoot);
            _scrolls.RefreshLoot(LootView());
            ShowBag(true);
            Refresh();
        }

        private IReadOnlyList<ItemStack> LootView() => _loot as IReadOnlyList<ItemStack> ?? new List<ItemStack>(_loot);

        private bool LootLeft()
        {
            foreach (var stack in _loot)
                if (stack.Amount > 0) return true;
            return false;
        }

        private void TakeLoot(int index)
        {
            if (_loot == null || index < 0 || index >= _loot.Count || _loot[index].Amount <= 0) return;
            var left = _lootTake != null ? _lootTake(index) : _loot[index].Amount;
            if (_loot == null) return;
            _loot[index].Amount = Mathf.Max(0, left);
            if (left > 0) Notice(RaidText.Get("not_enough_room", "Not enough room!") + " " + RaidText.Get("str_discard_item_instructions", "[SHIFT-CLICK] on this item to discard it."));
            if (!LootLeft()) CloseLoot();
            else _scrolls.RefreshLoot(LootView());
            Refresh();
        }

        private void TakeAllLoot()
        {
            if (_loot == null) return;
            for (var i = 0; _loot != null && i < _loot.Count; i++)
            {
                if (_loot[i].Amount <= 0 || _lootTake == null) continue;
                var left = _lootTake(i);
                if (_loot != null) _loot[i].Amount = Mathf.Max(0, left);
            }
            if (_loot == null) return;
            if (!LootLeft())
            {
                CloseLoot();
                return;
            }
            Notice(RaidText.Get("not_enough_room_confirm", "You don't have enough room in inventory to take all. Make room or choose only what you can carry."));
            _scrolls.RefreshLoot(LootView());
            Refresh();
        }

        private void CloseLoot()
        {
            var done = _lootDone;
            _loot = null;
            _lootTake = null;
            _lootDone = null;
            _scrolls.Close();
            done?.Invoke();
        }

        // ---- the party ---------------------------------------------------------------------------------

        private static ActorInstance Actor(uint guid)
        {
            return guid != 0u ? SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance.GetLibraryElement(guid) : null;
        }

        private void OnHero(uint guid, bool right)
        {
            if (right)
            {
                // the right button puts a chosen "move" away first, as it puts a chosen skill away in a fight
                if (_moving) PutAway();
                else ShowSheet(guid);
                return;
            }
            // at a camp a click picks whose skills are shown, or the companion a skill is for
            if (CampScreen.HeroClicked(guid))
            {
                Refresh();
                return;
            }
            var actor = Actor(guid);
            if (actor == null || !actor.IsLiving) return;
            // "move" is chosen: the companion clicked is whose place the hero takes, and the heroes go to their new
            // places (CorridorView.Reorder); the hero stays the selected one. A click on the hero themself puts
            // the skill away.
            if (_moving)
            {
                _moving = false;
                _skills = null;
                if (guid != _selected) DungeonRun.Current?.MoveHero(_selected, guid);
                Refresh();
                return;
            }
            _selected = guid;
            Refresh();
        }

        /// <summary>
        /// Escape is the game's key for its menu as well. True when the key has "move" to put away (it is put
        /// away), and for the frame in which this screen heard the key first and did so: the menu stays shut for
        /// that press (<see cref="EscapePutsMoveAway"/>).
        /// </summary>
        internal static bool TakesEscape()
        {
            var hud = _instance;
            if (hud == null || !hud.gameObject.activeSelf) return false;
            if (hud._moving)
            {
                hud._escapeFrame = Time.frameCount;
                hud.PutAway();
                return true;
            }
            return Time.frameCount - hud._escapeFrame <= 1;
        }

        // "Move", in the narrow place beside the banner's five skills: outside a fight it is the one skill that
        // can be used. Chosen, the companions' places are marked and it waits for one of them; chosen again (or
        // Escape, or the right button on a hero, a mark or the panels), it is put away.
        private void OnMoveSkill()
        {
            var run = DungeonRun.Current;
            _moving = !_moving && run != null && run.CanReorder && !Asking && !Picking && !CampScreen.IsOpen;
            _skills = null;
            Refresh();
        }

        /// <summary>
        /// Dev bridge: the banner's row of skills as it is and the places "move" has marked; with a place (1 to 5;
        /// 6: "move", in the narrow place) that place is clicked.
        /// </summary>
        public static object DevSkill(int place)
        {
            if (_instance == null) return "no HUD";
            var hud = _instance;
            if (place > 0)
            {
                var skills = CampScreen.IsOpen ? CampScreen.Abilities(hud._selected) : hud._skills;
                if (skills == null || place > skills.Count) return "no such place";
                if (skills[place - 1].Click == null) return "that place does nothing";
                skills[place - 1].Click();
            }
            var row = new List<object>();
            var shown = CampScreen.IsOpen ? CampScreen.Abilities(hud._selected) : hud._skills;
            if (shown != null)
                foreach (var skill in shown)
                    row.Add(new { id = skill.Id, art = skill.Icon != null, dark = skill.Dim, chosen = skill.Chosen, narrow = skill.Narrow, clicks = skill.Click != null });
            return new { moving = hud._moving, selected = hud._selected, row, marks = hud._marks.Describe(), ranksMoving = CorridorView.RanksMoving };
        }

        // DD1: a right click on a hero opens the character sheet. Not while the expedition is in the middle
        // of something: the sheet is a screen of DD2's own.
        private void ShowSheet(uint guid)
        {
            var run = DungeonRun.Current;
            if (run == null || Asking || Picking || CampScreen.IsOpen || !Dd2.EstateSession.InHub) return;
            var actor = Actor(guid);
            if (actor == null || !actor.IsLiving) return;
            try { UpgradeUi.ShowSheet(guid); }
            catch (Exception e) { Plugin.Log.LogWarning("Dungeon HUD: the character sheet could not be opened: " + e.Message); }
        }

        /// <summary>DD2's character sheet is over the screen: what a key would do in the dungeon under it waits.</summary>
        public static bool SheetOpen
        {
            get
            {
                try
                {
                    return SingletonMonoBehaviour<Assets.Code.UI.Managers.CommonUiBhv>.HasInstance() && SingletonMonoBehaviour<Assets.Code.UI.Managers.CommonUiBhv>.Instance.IsCharacterSheetOpen;
                }
                catch (Exception) { return false; }
            }
        }

        // DD1's C shows the selected hero's sheet and puts it away again. The sheet is DD2's own screen, and
        // is put away the way its own close button does it (CommonUiBhv.HideCharacterSheet).
        private void ToggleSheet()
        {
            if (!SheetOpen)
            {
                ShowSheet(_selected);
                return;
            }
            try { SingletonMonoBehaviour<Assets.Code.UI.Managers.CommonUiBhv>.Instance.HideCharacterSheet(); }
            catch (Exception e) { Plugin.Log.LogWarning("Dungeon HUD: the character sheet could not be put away: " + e.Message); }
        }

        // ---- the bag -----------------------------------------------------------------------------------

        // DD1's inventory under the pointer: the RIGHT button uses an item ("[RIGHT-CLICK] on Anti-Venom or
        // Bandages in inventory to heal", "[RIGHT-CLICK] an inventory item to apply it to the object": on the
        // selected hero, or on the curio or obstacle whose scroll is up); the left button picks a card up to carry
        // it (OnBagDragBegan), and a plain click with it uses the item as well (DD1's help says "[CLICK] on
        // firewood in inventory ... to start CAMPING"; what a plain click does to the other items was not seen:
        // it is read as the same use). A shift-click throws the stack away.
        private void OnBagClicked(int slot, bool right)
        {
            var run = DungeonRun.Current;
            if (run == null) return;
            var stack = run.Bag.Slot(slot);
            if (Picking)
            {
                if (right) Pick(-1);
                else if (stack != null) Pick(slot);
                Refresh();
                return;
            }
            if (stack == null)
            {
                PutAway();
                return;
            }
            // DD1: a shift-click throws the stack away
            if (!right && RaidUi.ShiftHeld)
            {
                Discard(run, slot);
                Refresh();
                return;
            }
            Use(run, slot, _selected);
        }

        // An item of the bag is used: offered to the scroll that has a slot for one, else used on a hero (or by
        // the party: a torch, the firewood).
        private void Use(DungeonRun run, int slot, uint hero)
        {
            var stack = run.Bag.Slot(slot);
            if (stack == null) return;
            // DD1: a trinket goes between the inventory and a hero's two slots anywhere outside a fight, also
            // while a loot window is up (that is how room is made for what lies on it)
            if (stack.Item.Type == ItemTypes.Trinket)
            {
                WearFromBag(run, slot);
                Refresh();
                return;
            }
            if (_promptAnswer != null && _prompt != null && _prompt.TakesItems && (_prompt.Kind == RaidPromptKind.Obstacle ? stack.Item == _prompt.SlotItem : stack.Item.Type != ItemTypes.Provision || run.CurioTakes(stack.Item)))
            {
                Offer(slot);
                return;
            }
            if (_loot != null && !DungeonRun.IsUsedItem(stack.Item))
            {
                Notice(RaidText.Get("str_discard_item_instructions", "[SHIFT-CLICK] on this item to discard it."));
                return;
            }
            // DD1: what a hero takes goes to the selected one
            if (!run.UseItem(slot, run.NeedsHero(slot) ? hero : 0u, out var message)) Notice(message);
            Refresh();
        }

        // ---- a card carried ----------------------------------------------------------------------------

        private void OnBagDragBegan(int slot)
        {
            var run = DungeonRun.Current;
            var stack = run?.Bag.Slot(slot);
            if (stack == null || Picking) return;
            _carried = slot;
            _bag.Carried = slot;
            _tooltip.Hide(_bag);
            _ghost.sprite = InventoryContent.Icon(stack.Item, stack.Amount);
            // DD1's colour for the card in hand (inventory_selected: .hvec4 1.4 1.4 1.7: lighter and bluer than its
            // art); a UI picture's own colour cannot pass white, so it is drawn through the lit material
            _ghost.material = RaidHighlight.Tinted("inventory_selected", new Color(1.4f, 1.4f, 1.7f, 1f));
            _ghost.color = _ghost.sprite != null ? Color.white : new Color(0.25f, 0.2f, 0.12f);
            // a trinket's card goes with DD2's picture of the trinket on it, as it lay in the bag
            TrinketPicture.Show(_ghost, stack.Item.Type == ItemTypes.Trinket ? stack.Item.Id : null);
            _ghost.gameObject.SetActive(true);
            _ghost.transform.SetAsLastSibling();
            _scrolls.LightSlot(true);
        }

        private void OnBagDragMoved(int slot, Vector2 screen)
        {
            if (_carried != slot) return;
            GhostTo(screen);
        }

        // The card in the player's hand follows the pointer (screen pixels).
        private void GhostTo(Vector2 screen)
        {
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_screen, screen, null, out var local))
                ((RectTransform)_ghost.transform).anchoredPosition = new Vector2(local.x + 960f, local.y - 1080f);
        }

        // The card is let go: over the scroll's slot it is offered to it, over a hero it is used on that hero,
        // over another cell of the bag it is put there (stacks of a kind join, two kinds change places);
        // anywhere else it goes back.
        private void OnBagDragEnded(int slot, Vector2 screen)
        {
            if (_carried != slot) return;
            _carried = -1;
            _bag.Carried = -1;
            _ghost.gameObject.SetActive(false);
            _scrolls.LightSlot(false);
            var run = DungeonRun.Current;
            if (run == null || run.Bag.Slot(slot) == null) return;
            Drop(run, slot, DropTarget(screen), false);
            Refresh();
        }

        // Where a carried card is let go: "slot" (the scroll's), "hero:<guid>", "cell:<n>", "worn:<n>" (one of the
        // selected hero's two trinket slots), or null.
        private string DropTarget(Vector2 screen)
        {
            var scrollSlot = _scrolls.ItemSlot;
            if (scrollSlot != null && RectTransformUtility.RectangleContainsScreenPoint(scrollSlot, screen, null)) return "slot";
            var cell = _bagSide.activeInHierarchy ? _bag.SlotAt(screen) : -1;
            if (cell >= 0) return "cell:" + cell;
            var worn = _hero.TrinketSlotAt(screen);
            if (worn >= 0) return "worn:" + worn;
            var hero = _trays.HeroAt(screen);
            if (hero == 0u && _hero.PortraitAt(screen)) hero = _selected;
            return hero != 0u ? "hero:" + hero : null;
        }

        private bool Drop(DungeonRun run, int slot, string target, bool dev)
        {
            if (target == null) return false;
            if (target == "slot") return Offer(slot);
            if (target.StartsWith("cell:", StringComparison.Ordinal) && int.TryParse(target.Substring(5), out var cell)) return run.Bag.Move(slot, cell);
            if (target.StartsWith("worn:", StringComparison.Ordinal) && int.TryParse(target.Substring(5), out var worn))
            {
                // DD1 carries a trinket from the inventory onto one of the hero's two slots: it lies there, and what
                // lay there takes its place in the inventory
                if (run.Bag.Slot(slot)?.Item.Type != ItemTypes.Trinket || _selected == 0u) return false;
                var refusal = RealmInventory.WearFromBag(_selected, slot, worn);
                if (refusal != null) Notice(refusal);
                // (the pointer rests on the slot: the tooltip it called up on the way there is of what lay in it before)
                else _tooltip.HideAll();
                return refusal == null;
            }
            if (target.StartsWith("hero:", StringComparison.Ordinal) && uint.TryParse(target.Substring(5), out var hero))
            {
                // DD1 carries a trinket from the inventory onto a hero: it is put on (into a free slot of theirs)
                if (run.Bag.Slot(slot)?.Item.Type == ItemTypes.Trinket)
                {
                    var refusal = RealmInventory.WearFromBag(hero, slot);
                    if (refusal != null) Notice(refusal);
                    return refusal == null;
                }
                // carried to a hero: what a hero takes is that hero's
                if (!run.NeedsHero(slot)) return false;
                var used = run.UseItem(slot, hero, out var message);
                if (!used) Notice(message);
                return used;
            }
            return false;
        }

        // ---- a worn trinket carried ----------------------------------------------------------------------

        // DD1 drags trinkets between the inventory and the hero's two slots anywhere outside a fight. A worn one
        // is picked up from its slot on the hero panel (RaidHeroPanel's carry) with the bag's own card in hand.
        private int _wornCarried = -1;

        private bool CanCarryWorn(int index)
        {
            if (_carried >= 0 || _wornCarried >= 0 || Picking || _selected == 0u || DungeonRun.Current == null) return false;
            var id = _hero.TrinketIn(index);
            // (its own slot is always a place for it: what is asked is whether it may be moved at all, here and now)
            return id != null && RealmInventory.MoveWornRefusal(_selected, id, index) == null;
        }

        private void OnWornDragBegan(int index)
        {
            var id = _hero.TrinketIn(index);
            if (id == null) return;
            _wornCarried = index;
            _held = -1;
            _tooltip.HideAll();
            _ghost.sprite = RaidUi.Sprite(Trinkets.RarityArt(Trinkets.Grade(id)));
            _ghost.material = RaidHighlight.Tinted("inventory_selected", new Color(1.4f, 1.4f, 1.7f, 1f));
            _ghost.color = _ghost.sprite != null ? Color.white : new Color(0.25f, 0.2f, 0.12f);
            TrinketPicture.Show(_ghost, id);
            _ghost.gameObject.SetActive(true);
            _ghost.transform.SetAsLastSibling();
        }

        // The card is let go: over the hero's other slot the two change places (also with nothing), over the
        // inventory it comes off into it; anywhere else it goes back.
        private void OnWornDragEnded(int index, Vector2 screen)
        {
            if (_wornCarried != index) return;
            _wornCarried = -1;
            _ghost.gameObject.SetActive(false);
            var slot = _hero.TrinketSlotAt(screen);
            var bag = _bagSide.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)_bagSide.transform, screen, null);
            DropWorn(index, slot >= 0 ? "worn:" + slot : bag ? "bag" : null);
            Refresh();
        }

        private bool DropWorn(int index, string target)
        {
            var id = _hero.TrinketIn(index);
            if (target == null || id == null || _selected == 0u || DungeonRun.Current == null) return false;
            string refusal;
            if (target == "bag") refusal = RealmInventory.TakeOffToBag(_selected, id);
            else if (target.StartsWith("worn:", StringComparison.Ordinal) && int.TryParse(target.Substring(5), out var slot))
            {
                if (slot == index) return false;
                refusal = RealmInventory.MoveWorn(_selected, id, slot);
            }
            else return false;
            if (refusal != null) Notice(refusal);
            // (a tooltip the pointer called up on the way is of what lay there before)
            else _tooltip.HideAll();
            return refusal == null;
        }

        /// <summary>Dev bridge: the trinket in a slot of the selected hero is carried and let go ({"slot":0,"to":"worn:1"|"bag"}).</summary>
        public static object DevWorn(int index, string to)
        {
            if (_instance == null || DungeonRun.Current == null) return "no expedition";
            if (_instance._hero.TrinketIn(index) == null) return "nothing in that slot";
            var can = _instance.CanCarryWorn(index);
            var done = can && _instance.DropWorn(index, to);
            _instance.Refresh();
            return new { canCarry = can, done, worn = RealmInventory.Worn(_instance._selected), notice = _instance._notice };
        }

        /// <summary>
        /// Dev bridge: a card of the bag is used as the right button uses it ({"use":slot}), or carried and let go
        /// ({"drag":slot,"to":"slot"|"hero:GUID"|"cell:N"|"worn:N"}).
        /// </summary>
        public static object DevBag(int slot, string to)
        {
            var run = DungeonRun.Current;
            if (_instance == null || run == null) return "no expedition";
            if (run.Bag.Slot(slot) == null) return "nothing in that slot";
            bool done;
            if (to == null)
            {
                _instance.Use(run, slot, _instance._selected);
                done = true;
            }
            else done = _instance.Drop(run, slot, to, true);
            _instance.Refresh();
            return new { done, hud = Describe() };
        }

        // A click on a trinket of the bag: onto the selected hero. With both of the hero's slots taken the card
        // is held (it keeps the pointer's frame), and a click on one of the two worn changes places with it.
        private void WearFromBag(DungeonRun run, int slot)
        {
            _held = -1;
            var stack = run.Bag.Slot(slot);
            if (stack == null || _selected == 0u) return;
            var refusal = RealmInventory.WearFromBagRefusal(_selected, slot);
            if (refusal == null)
            {
                refusal = RealmInventory.WearFromBag(_selected, slot);
                if (refusal != null) Notice(refusal);
                return;
            }
            // not a matter of room: this hero cannot wear it, or nobody can right now
            if (RealmInventory.WearFromBagRefusal(_selected, slot, 0) != null && RealmInventory.WearFromBagRefusal(_selected, slot, 1) != null)
            {
                Notice(refusal);
                return;
            }
            _held = slot;
            Notice("Both trinket slots are taken: click the worn trinket to change it for.");
        }

        // A click on a trinket the shown hero wears: it changes places with the bag's trinket that is held, or
        // comes off into the bag.
        private void OnWornTrinket(uint guid, string id)
        {
            var run = DungeonRun.Current;
            if (run == null || guid == 0u || id == null || Picking) return;
            string refusal;
            if (_held >= 0 && run.Bag.Slot(_held)?.Item.Type == ItemTypes.Trinket) refusal = RealmInventory.WearFromBag(guid, _held, RealmInventory.Worn(guid).IndexOf(id));
            else refusal = RealmInventory.TakeOffToBag(guid, id);
            _held = -1;
            if (refusal != null) Notice(refusal);
            _tooltip.HideAll();
            Refresh();
        }

        private void Discard(DungeonRun run, int slot)
        {
            var stack = run.Bag.Slot(slot);
            if (stack == null) return;
            if (stack.Item.Type == ItemTypes.QuestItem)
            {
                Notice(RaidText.Get("cant_discard_quest_item_confirm", "You cannot discard quest items."));
                return;
            }
            var dropped = run.Bag.Clear(slot);
            if (dropped != null) Notice("Dropped: " + InventoryText.Counted(dropped.Item, dropped.Amount) + ".");
        }

        // DD1 hangs an item's tooltip beside its card (inventory_item_tooltip_layout).
        private void OnBagHovered(int slot)
        {
            _hover = slot;
            var run = DungeonRun.Current;
            var stack = slot >= 0 && run != null ? run.Bag.Slot(slot) : null;
            if (stack == null)
            {
                _tooltip.Hide(_bag);
                return;
            }
            var l = RaidLayout.Current;
            // DD1's words for the item; a quest item cannot be thrown away, so it is not told how
            var words = InventoryText.Tooltip(stack.Item, stack.Item.Type != ItemTypes.QuestItem);
            _tooltip.Show(_bag, RaidTooltip.Titled(InventoryText.Name(stack.Item), words), l.RightPanel + l.BagStart + _bag.SlotPosition(slot) + l.ItemTooltipOffset, l.ItemTooltipWidth);
        }

        private void PutAway()
        {
            if (Picking) Pick(-1);
            if (_moving)
            {
                _moving = false;
                _skills = null;
            }
            _held = -1;
            _notice = null;
            Refresh();
        }

        private void Notice(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            _notice = text;
            _noticeUntil = Time.unscaledTime + RaidLayout.Current.UserInformationTime;
            _noticeAt = PointerAt();
            ShowNotice();
        }

        // ---- per frame ---------------------------------------------------------------------------------

        private void Update()
        {
            var keyboard = Keyboard.current;
            // under DD1's loading screen any key sends that screen away, and does nothing else
            if (keyboard != null && !Dd2.LoadingScreen.IsUp)
            {
                if (keyboard.escapeKey.wasPressedThisFrame)
                {
                    if (_confirm.IsOpen) _confirm.Close();
                    else if (_quest.ChoiceOpen) _quest.HideChoice();
                    else if (Picking || _moving)
                    {
                        if (_moving) _escapeFrame = Time.frameCount;
                        PutAway();
                    }
                }
                // DD1's key for a torch
                if (keyboard.tKey.wasPressedThisFrame && !Asking && !Picking && !_confirm.IsOpen && !CampScreen.IsOpen) DungeonRun.Current?.UseTorch();
                // DD1: "TAB - alternate Map and Inventory" (while a stack is being picked the inventory stays)
                if (keyboard.tabKey.wasPressedThisFrame && !Picking && !_confirm.IsOpen) ShowBag(!_showBag);
                // DD1: "SPACE - take ALL when LOOT window is up"
                if (keyboard.spaceKey.wasPressedThisFrame && _loot != null && !_confirm.IsOpen) TakeAllLoot();
                // DD1: "C - Character (hero) panel details"
                if (keyboard.cKey.wasPressedThisFrame && !_confirm.IsOpen) ToggleSheet();
            }
            // DD1 (seen in its own frames): the brackets, the bars and the quest's words are gone from the frame a door is
            // used, and back a moment after the next area stands in full light
            if (CorridorView.TransitionRunning) _overlaysBack = Time.unscaledTime + CorridorNumbers.OverlayReturn;
            var overlays = Time.unscaledTime >= _overlaysBack;
            _trays.Visible = overlays;
            _quest.Visible = overlays || _quest.ChoiceOpen;
            // DD1's scripts take the HUD over the scene away while a hero is shown close (layers.HUD_alpha): the
            // bars and brackets, the quest's corner and the torch; the two panels stay. And the torch fades with
            // the scene on the way through a door (seen in DD1's recordings: gone at black, back with the next area).
            var hud = Mathf.Clamp01(CorridorView.HudAlpha);
            _trays.Alpha = hud;
            _quest.Alpha = hud;
            _torch.Alpha = hud * (CorridorView.TransitionRunning ? Mathf.Clamp01(CorridorLight.Fade) : 1f);
            _quest.Tick(Time.unscaledDeltaTime);
            // the bars of heroes who change places pass through one another with them
            _trays.KeepApart = !CorridorView.RanksMoving;
            _trays.Follow();
            _marks.Follow();
            _torch.Flicker();
            _pops.Tick(Time.unscaledDeltaTime);
            _bark.Tick(Time.unscaledDeltaTime);
            _announce.Tick(Time.unscaledDeltaTime);
            if (Time.unscaledTime < _nextRefresh) return;
            Refresh();
        }

        // Heroes who change places are placed by the corridor's own Update, which may come after this screen's:
        // their bars are put under them once more when everything has moved, so that none trails its hero.
        private void LateUpdate()
        {
            if (_trays != null && CorridorView.RanksMoving) _trays.Follow();
        }

        private void Refresh()
        {
            _nextRefresh = Time.unscaledTime + 0.25f;
            if (_screen == null) return;
            var run = DungeonRun.Current;

            // who marches: the trays are rebuilt when the party is not the one shown (a death, a new expedition)
            var library = SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance;
            var guids = Singleton<GameTypeMgr>.Instance.RosterManager.GetActorGuids(RosterStatusType.PARTY);
            var living = new List<uint>();
            foreach (var guid in guids)
            {
                var actor = library.GetLibraryElement(guid);
                if (actor != null && actor.IsLiving) living.Add(guid);
            }
            var same = living.Count == _party.Count;
            for (var i = 0; same && i < living.Count; i++) same = living[i] == _party[i];
            if (!same)
            {
                _party.Clear();
                _party.AddRange(living);
                _trays.Rebuild(guids);
            }

            var camp = CampScreen.IsOpen ? Camp.Current : null;
            if (camp != null && living.Contains(camp.Selected)) _selected = camp.Selected;
            if (!living.Contains(_selected)) _selected = living.Count > 0 ? living[0] : 0u;
            var hero = Actor(_selected);
            _hero.Show(hero);
            if (camp != null) _hero.ShowAbilities(CampScreen.Abilities(_selected));
            else
            {
                // "move" waits for a companion only while the party can change its order
                if (_moving && (run == null || !run.CanReorder || Asking || Picking))
                {
                    _moving = false;
                    _skills = null;
                }
                if (_skills == null || _skillsOf != _selected || Time.unscaledTime >= _skillsAt)
                {
                    _skills = RaidHeroPanel.CombatSkills(hero, _moving, OnMoveSkill);
                    _skillsOf = _selected;
                    _skillsAt = Time.unscaledTime + SkillsEvery;
                }
                _hero.ShowAbilities(_skills);
            }
            _trays.Refresh(_selected, camp != null ? CampScreen.IsTarget : (Func<uint, bool>)null);
            // "move" is chosen: every companion's place is one the hero can take
            _marks.Show(_moving && camp == null ? living.FindAll(guid => guid != _selected) : null);

            ShowOrderButton(run);
            if (run == null) return;
            _torch.Show(run.Exploration.Light, run.Exploration.LightBand, run.LightBands);
            // on a display narrower than DD1's 16:9 the screen's corners are cut off: what stands in the top left one moves in
            var cut = Mathf.Max(0f, (1920f - ((RectTransform)transform).rect.width) * 0.5f);
            _quest.Shift(cut);
            ((RectTransform)_log.transform).anchoredPosition = new Vector2(LogPos.x + cut, -LogPos.y);
            _quest.Show(run);

            ShowBagCards(run);
            // a held trinket keeps the pointer's frame while it waits for a slot of the hero
            if (_held >= 0 && (run.Bag.Slot(_held) == null || run.Bag.Slot(_held).Item.Type != ItemTypes.Trinket)) _held = -1;
            _bag.Selected = _held;
            _hero.TrinketHint = _held >= 0 ? "[CLICK] to change it for the trinket chosen in the inventory." : "[CLICK] to take it off, into the inventory.";
            if (_hover >= 0 && run.Bag.Slot(_hover) == null) _tooltip.Hide(_bag);
            _scrolls.RefreshSlot();

            ShowLog(run);
            ShowNotice();

            // DD1 offers the way home the moment the goal is met; later the seal in the corner does
            if (_completeFor != run)
            {
                _completeFor = run;
                _completeTold = run.Exploration.ObjectiveComplete;     // a loaded expedition that was complete already
            }
            if (!_completeTold && run.Exploration.ObjectiveComplete && !run.Busy && !Asking && !Picking)
            {
                _completeTold = true;
                _quest.ShowChoice();
            }
        }

        // The bag's cards as DD1 draws them (seen in its own frames, _lab/dd1_ref/raid): a supply or the food that
        // cannot be used at the moment is grey (inventory_unselectable), whatever else the bag holds is as it
        // is. While a curio's scroll is up every supply can be tried on it and is lit, and wears DD1's mark of
        // what it is known to do to that curio (CurioMemory); the food, which goes on no curio but one, is grey.
        private void ShowBagCards(DungeonRun run)
        {
            var curio = _promptAnswer != null && _prompt != null && _prompt.Kind == RaidPromptKind.Curio && _prompt.TakesItems ? _prompt : null;
            for (var i = 0; i < _bag.SlotCount; i++)
            {
                var stack = run.Bag.Slot(i);
                var item = stack?.Item;
                bool dim;
                if (item == null || !DungeonRun.IsUsedItem(item)) dim = false;
                else if (curio != null) dim = item.Type == ItemTypes.Provision && !run.CurioTakes(item);
                else dim = !run.CanUse(i, _selected);
                _bag.Set(i, item, stack?.Amount ?? 0, dim);
                _bag.SetMark(i, curio != null && item != null ? CurioMemory.Art(curio.TrackerCurio, item) : null);
            }
        }

        // The last lines of the expedition's log, for a while after the newest was written.
        private void ShowLog(DungeonRun run)
        {
            if (_log.gameObject.activeSelf != ShowLogLines) _log.gameObject.SetActive(ShowLogLines);
            if (!ShowLogLines) return;
            var text = run.LogText(LogLines);
            if (text != _logShown)
            {
                _logShown = text;
                _logAt = Time.unscaledTime;
                _log.text = text;
            }
            var age = Time.unscaledTime - _logAt;
            var alpha = Mathf.Clamp01((LogSeconds - age) / LogFade);
            var colour = UiKit.Neutral;
            colour.a = alpha;
            _log.color = colour;
        }

        // DD1's line of user information: a few words over the pointer for user_information_popup.time seconds
        // (what to pick, why a click did nothing). It stands where the pointer was when it was said.
        private void ShowNotice()
        {
            string text = null;
            if (Picking) text = _pickText;
            else if (_notice != null && Time.unscaledTime < _noticeUntil) text = _notice;
            if (text == null)
            {
                if (_noticeText.gameObject.activeSelf) _noticeText.gameObject.SetActive(false);
                return;
            }
            if (_noticeText.text != text || !_noticeText.gameObject.activeSelf)
            {
                _noticeText.text = text;
                var wanted = _noticeText.GetPreferredValues(text, NoticeWidth, 0f);
                var size = new Vector2(Mathf.Min(NoticeWidth, Mathf.Ceil(wanted.x) + 2f), Mathf.Ceil(wanted.y));
                // the line's middle over the pointer, its foot y_offset above it; kept inside the screen
                var at = new Vector2(Mathf.Clamp(_noticeAt.x, size.x * 0.5f + 8f, 1920f - size.x * 0.5f - 8f), Mathf.Max(8f, _noticeAt.y + RaidLayout.Current.UserInformationY - size.y));
                ((RectTransform)_noticeText.transform).PlaceTopLeft(at, RaidUi.TopCentre, size);
                _noticeText.gameObject.SetActive(true);
                _noticeText.transform.SetAsLastSibling();
            }
        }

        // Where the pointer is on DD1's screen (y down); the screen's middle when there is no pointer to ask.
        private Vector2 PointerAt()
        {
            var mouse = Mouse.current;
            if (mouse != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(_screen, mouse.position.ReadValue(), null, out var local))
                return new Vector2(local.x + 960f, 1080f - local.y);
            return new Vector2(960f, 540f);
        }

        // ---- dev bridge --------------------------------------------------------------------------------

        public static object Describe()
        {
            if (_instance == null) return new { built = false };
            var hud = _instance;
            return new
            {
                built = true,
                shown = hud.gameObject.activeSelf,
                selected = hud._selected,
                moving = hud._moving,
                party = hud._party,
                tab = hud._showBag ? "inventory" : "map",
                asking = AskingExpedition,
                held = Asking,
                picking = Picking,
                prompt = PromptText,
                options = PromptOptions,
                window = hud._loot != null ? "loot" : hud._prompt != null ? hud._prompt.Kind.ToString().ToLowerInvariant() : null,
                title = hud._prompt?.Title,
                body = hud._prompt?.Body,
                roles = hud._prompt != null ? Array.ConvertAll(hud._prompt.Roles, r => r.ToString().ToLowerInvariant()) : null,
                loot = hud._loot != null ? ProvisionScreen.StackList(hud._loot) : null,
                questComplete = hud._quest.ChoiceOpen,
                questCompleteBlurred = hud._quest.ChoiceBlurred,
                campBonus = hud._quest.CampBonus,
                confirm = hud._confirm.IsOpen,
                confirmBlurred = hud._confirm.Blurred,
                notice = hud._notice != null && Time.unscaledTime < hud._noticeUntil ? hud._notice : null
            };
        }

        /// <summary>Dev bridge: a card of the loot scroll (-1: Take All), as a click on it.</summary>
        public static bool DevLoot(int index)
        {
            if (_instance == null || _instance._loot == null) return false;
            if (index < 0) _instance.TakeAllLoot();
            else _instance.TakeLoot(index);
            return true;
        }

        /// <summary>Dev bridge: the quest-complete choice, the retreat question and the loot scroll's Close.</summary>
        public static string DevPress(string what)
        {
            if (_instance == null) return "no HUD";
            switch (what)
            {
                case "retreat": _instance.AskRetreat(); return _instance._confirm.IsOpen ? "asked" : "not now";
                case "choice": _instance._quest.ShowChoice(); return "shown";
                case "continue": _instance._quest.HideChoice(); return "hidden";
                case "close": if (_instance._loot != null) _instance.CloseLoot(); _instance._confirm.Close(); return "closed";
                case "home": _instance.ShowBag(false); _instance._map.CentreOnParty(); return "centred";
                case "order": return DungeonRun.Current != null && DungeonRun.Current.RestoreOrder() ? "the party stands in its own order again" : "nothing to put back";
                default: return "unknown: retreat, choice, continue, close, home, order";
            }
        }
    }
}
