using System.Collections.Generic;
using System.Text;
using DD2Estate.Core;
using DD2Estate.Dd2;
using DD2Estate.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// The camp's screen, over the dungeon HUD while a camp lasts. DD1 plays a camp in two scrolls and the hero
    /// banner, and so does this:
    ///
    /// - the Repast scroll (scrolls/meal_scroll.png): the four meals as food cards, where DD1's
    ///   scripts/layout/screen.raid.darkest (meal_scroll) puts them, each told about in a tooltip at
    ///   meal_scroll.tooltipOffset;
    /// - the Respite scroll (scrolls/event_scroll_campingrespite.png): the points left and the REST button
    ///   (overlays/announcement_rest.png), at the offsets of the same file's camp_layout;
    /// - the selected hero's camping skills in the five places of the HUD's banner, where DD1 shows them at a
    ///   camp; what a skill costs and how often it can still be used is in its tooltip, as in DD1
    ///   (<see cref="Abilities"/>). A click on a hero in the scene (on the seated model, <see cref="CampHeroes"/>,
    ///   or on the bars under it) picks the hero whose skills are shown, or the companion a skill is for.
    ///
    /// Text is set in DD1's styles (scroll_header, scroll_body, camping_points, rest, inventory_amount).
    /// The scrolls hang at the right of the screen instead of DD1's centre (camp_layout.respite_scroll_pos):
    /// DD2's rest stop, shown behind, seats the party left of centre and leaves that side empty. Under the
    /// scroll the mod keeps a box of its own for what DD1 says in pop-ups over the heroes: what a skill or a
    /// meal just did, and what to do next.
    /// tools/preview_raid_hud.py draws the same layout offline: change one, change the other.
    /// </summary>
    internal class CampScreen : MonoBehaviour
    {
        // Reference pixels (1920x1080), y down.
        private const float PanelHeight = 360f;             // the HUD's panels: 1080 - screen_guide.panel_top
        private static readonly Vector2 MealScrollSize = new Vector2(456f, 333f);       // scrolls/meal_scroll.png
        private static readonly Vector2 RespiteScrollSize = new Vector2(456f, 237f);    // scrolls/event_scroll_campingrespite.png
        private static readonly Vector2 RestArt = new Vector2(257f, 68f);               // overlays/announcement_rest.png

        // The mod's own numbers.
        private static readonly Vector2 ScrollPos = new Vector2(1420f, 40f);            // see the class comment
        private const float HeaderWidth = 370f;             // the band between a scroll's two gold posts
        private const float NoteGap = 44f;                  // under the scroll in use (the REST button hangs out of its scroll)
        private const float NoteWidth = 456f;
        private const float NotePad = 14f;
        private const int LogLines = 3;
        private const float MealNameRise = 26f;             // a meal's name stands on the foot of its card
        private const float MealTooltipGap = 36f;           // between a meal's tooltip and the scroll
        public const float FadeTime = 0.4f;

        private const string ScrollDir = "scrolls/";

        private static CampScreen _instance;

        private Camp _camp;
        private Image _cover;
        private RectTransform _screen, _scroll, _noteBack;
        private TextMeshProUGUI _note;
        private RaidTooltip _tooltip;
        private string _shownScroll;
        private string _message;
        private bool _messageIsWarning;
        private float _messageUntil;
        private float _nextRefresh;
        private float _shownSince = -1f;
        private float _nightSince = -1f;
        private bool _restArmed;

        public static bool IsOpen => _instance != null && _instance.gameObject.activeSelf && _instance._camp != null;

        public static void Open(Camp camp)
        {
            if (_instance == null)
            {
                var canvas = UiKit.Canvas("DD2Estate.Camp", 5);      // over the dungeon HUD (4)
                _instance = canvas.gameObject.AddComponent<CampScreen>();
                _instance.Build();
            }
            _instance._camp = camp;
            _instance._shownScroll = null;
            _instance._message = null;
            _instance._restArmed = false;
            _instance._shownSince = -1f;
            _instance._nightSince = -1f;
            _instance._cover.gameObject.SetActive(true);
            _instance._cover.color = Color.black;
            ((RectTransform)_instance._cover.transform).offsetMin = Vector2.zero;
            _instance.gameObject.SetActive(true);
            // the seated heroes take the pointer under the scrolls and over the HUD
            CampHeroes.Open(_instance.transform, _instance._screen);
            _instance.Refresh();
        }

        public static void Close()
        {
            if (_instance == null) return;
            CampHeroes.Close();
            _instance._camp = null;
            _instance._tooltip.HideAll();
            _instance.gameObject.SetActive(false);
        }

        /// <summary>
        /// Night falls: the dark comes back over the rest stop (not over the HUD) and stays until the screen is
        /// closed, so that the scene can be taken away behind it.
        /// </summary>
        public static void Darken()
        {
            if (!IsOpen || _instance._nightSince >= 0f) return;
            _instance._nightSince = Time.unscaledTime;
            var cover = _instance._cover;
            if (!cover.gameObject.activeSelf)
            {
                cover.color = new Color(0f, 0f, 0f, 0f);
                cover.gameObject.SetActive(true);
            }
            ((RectTransform)cover.transform).offsetMin = new Vector2(0f, PanelHeight);
            _instance.Refresh();
        }

        // ---- the dark before a night ambush ------------------------------------------------------------

        private static Canvas _dark;
        private static bool _darkHeld;

        /// <summary>The dark is held over the scene: on (for the dev bridge).</summary>
        public static bool DarkHeld => _darkHeld;

        /// <summary>
        /// The dark over the scene, under the HUD (whose "Ambush!" goes up over it), from the camp's end until
        /// the game's wipe into the night's fight has closed. It is taken away by itself: when the game has
        /// left the hub's mode (which it does behind its fader, the moment the wipe has closed; the fader's own
        /// "black" lasts a frame or two and is easily missed), when no fight came of it after all, or after a
        /// few seconds whatever happened. It must never outlast that: it lies over everything the game draws.
        /// </summary>
        public static void HoldDark()
        {
            if (_darkHeld) return;
            if (_dark == null)
            {
                _dark = UiKit.Canvas("DD2Estate.CampDark", 3);      // under the dungeon HUD (4)
                var raycaster = _dark.GetComponent<GraphicRaycaster>();
                if (raycaster != null) Destroy(raycaster);
                var sheet = UiKit.Image("Dark", _dark.transform, null, Color.black);
                sheet.raycastTarget = false;
                UiKit.Stretch((RectTransform)sheet.transform);
            }
            _darkHeld = true;
            _dark.enabled = true;
            Plugin.Host.StartCoroutine(HoldDarkUntilCovered());
        }

        private static System.Collections.IEnumerator HoldDarkUntilCovered()
        {
            var until = Time.unscaledTime + 8f;
            var idle = 0f;
            while (Time.unscaledTime < until && EstateSession.Active)
            {
                // the game changes its mode behind its fade
                if (!EstateSession.HubMode) break;
                if (Assets.Code.Utils.SingletonMonoBehaviour<Assets.Code.UI.ScreenFaderBhv>.HasInstance()
                    && Assets.Code.Utils.SingletonMonoBehaviour<Assets.Code.UI.ScreenFaderBhv>.Instance.IsBlack) break;
                // the hub is in and nothing is under way: no fight came of it
                var run = DungeonRun.Current;
                idle = EstateSession.InHub && (run == null || !run.Busy) ? idle + Time.unscaledDeltaTime : 0f;
                if (idle > 1f) break;
                yield return null;
            }
            LiftDark();
        }

        /// <summary>The dark is taken away at once (also by the session, whenever the hub's mode is not the game's).</summary>
        public static void LiftDark()
        {
            _darkHeld = false;
            if (_dark != null) _dark.enabled = false;
        }

        // ---- construction ----------------------------------------------------------------------------

        private void Build()
        {
            // Hides the corridor giving way to the rest stop; fades once the scene is up. If the scene never
            // comes, it stays as the dark above the HUD.
            _cover = UiKit.Image("Cover", transform, null, Color.black, true);
            UiKit.Stretch((RectTransform)_cover.transform);

            _screen = RaidUi.Screen("Screen", transform);
            _scroll = UiKit.Rect("Scroll", _screen).PlaceTopLeft(ScrollPos, RaidUi.TopLeft, MealScrollSize);
            var noteBack = UiKit.Image("NoteBack", _screen, RaidUi.Sliced("shared/tooltip/tooltip_background.png", Mathf.Round(128f * RaidLayout.Current.TooltipBorder)), Color.white);
            if (noteBack.sprite != null) noteBack.type = Image.Type.Sliced;
            else noteBack.color = new Color(0f, 0f, 0f, 0.87f);
            DD2Estate.UI.Dd2TooltipLook.Dress(noteBack);        // the note is a tooltip that stays: DD2's ground, as the tooltips have it
            _noteBack = (RectTransform)noteBack.transform;
            _note = UiKit.Text("Note", _noteBack, "", "tooltip", UiKit.Neutral, TextAlignmentOptions.TopLeft);
            _note.richText = true;
            _tooltip = new RaidTooltip(_screen);
        }

        // ---- what the HUD shows for the camp -----------------------------------------------------------

        /// <summary>A hero of the scene was clicked: the companion an armed skill is for, else the hero to show.</summary>
        public static bool HeroClicked(uint guid)
        {
            if (!IsOpen) return false;
            _instance.OnHero(guid);
            return true;
        }

        /// <summary>True for a hero the armed skill may be used on: the HUD marks them as DD1 marks a friendly target.</summary>
        public static bool IsTarget(uint guid)
        {
            if (!IsOpen) return false;
            var camp = _instance._camp;
            if (camp.Armed == null || camp.Closing || guid == camp.Selected) return false;
            var hero = camp.Session.Hero(guid);
            return hero != null && hero.Alive;
        }

        /// <summary>
        /// The selected hero's camping skills for the banner's five places, as DD1 shows them at a camp: the
        /// skill's own icon, dark when it cannot be used now. Its tooltip is DD1's: the name, "Time Cost: n",
        /// "Uses Remaining: n", then what it does (<see cref="CampText.Lines"/>).
        /// </summary>
        public static List<RaidAbility> Abilities(uint guid)
        {
            var list = new List<RaidAbility>();
            if (!IsOpen) return list;
            var screen = _instance;
            var camp = screen._camp;
            var session = camp.Session;
            var rules = CampContent.Rules;
            var hero = session.Hero(guid);
            if (hero == null) return list;
            var companions = session.Companions(hero.Id);
            foreach (var id in hero.Skills)
            {
                var skill = rules.Skill(id);
                if (skill == null) continue;
                var skillId = skill.Id;
                var refusal = camp.Closing ? "The party has turned in." : session.Refusal(hero.Id, skillId, skill.NeedsTarget && companions.Count > 0 ? companions[0].Id : 0u);
                var left = session.UsesLeft(hero.Id, skillId);
                var words = new StringBuilder();
                words.Append(CampText.Cost(skill)).Append('\n').Append(CampText.UsesLeft(left));
                var does = CampText.Describe(skill);
                if (does.Length > 0) words.Append('\n').Append(does);
                if (refusal != null) words.Append("\n<color=").Append(RaidText.Hex(UiKit.Harmful)).Append('>').Append(refusal).Append("</color>");
                list.Add(new RaidAbility
                {
                    Id = "camp." + skillId,
                    Icon = CampContent.Icon(skill),
                    Tooltip = RaidTooltip.Titled(CampText.Name(skill), words.ToString()),
                    Dim = refusal != null,
                    Chosen = camp.Armed == skillId,
                    Click = () => screen.OnSkill(skillId)
                });
            }
            return list;
        }

        // ---- input -----------------------------------------------------------------------------------

        private void OnHero(uint guid)
        {
            var hero = _camp.Session.Hero(guid);
            if (hero == null || !hero.Alive || _camp.Closing) return;
            if (_camp.Armed != null && guid != _camp.Selected)
            {
                Say(_camp.Use(_camp.Selected, _camp.Armed, guid));
            }
            else
            {
                _camp.Selected = guid;
                _camp.Armed = null;
            }
            CampHeroes.Chosen(guid);
            _restArmed = false;
            Refresh();
        }

        private void OnSkill(string id)
        {
            if (_camp == null) return;
            _restArmed = false;
            var skill = CampContent.Rules.Skill(id);
            if (skill == null) return;
            if (skill.NeedsTarget)
            {
                // refused for any reason but the missing companion: say so now
                var companions = _camp.Session.Companions(_camp.Selected);
                var refusal = _camp.Refusal(_camp.Selected, id, companions.Count > 0 ? companions[0].Id : 0u);
                if (refusal != null) Say(refusal);
                else _camp.Armed = _camp.Armed == id ? null : id;
            }
            else Say(_camp.Use(_camp.Selected, id));
            Refresh();
        }

        private void OnMeal(string type)
        {
            if (_camp == null) return;
            Say(_camp.Eat(type));
            Refresh();
        }

        // DD1 lets the party rest with points left; one more click says it was meant.
        private void OnRest()
        {
            if (_camp == null) return;
            if (!_restArmed && _camp.Session.Points > 0 && AnySkillUsable())
            {
                _restArmed = true;
                Say("Respite points remain. Click again to rest all the same.", false);
                return;
            }
            Say(_camp.Sleep());
            Refresh();
        }

        private bool AnySkillUsable()
        {
            foreach (var hero in _camp.Session.Party)
                foreach (var id in hero.Skills)
                {
                    var skill = CampContent.Rules.Skill(id);
                    if (skill == null) continue;
                    var companions = _camp.Session.Companions(hero.Id);
                    if (_camp.Session.Refusal(hero.Id, id, skill.NeedsTarget && companions.Count > 0 ? companions[0].Id : 0u) == null) return true;
                }
            return false;
        }

        private void Cancel()
        {
            if (_camp == null) return;
            _camp.Armed = null;
            _restArmed = false;
            Refresh();
        }

        // A refusal (null: nothing to say).
        private void Say(string trouble) => Say(trouble, true);

        private void Say(string text, bool warning)
        {
            _message = text;
            _messageIsWarning = warning;
            _messageUntil = Time.unscaledTime + 4f;
        }

        private void Update()
        {
            var run = DungeonRun.Current;
            if (_camp == null || run == null || run.Camp != _camp || !EstateSession.InHub || EstateSession.View != EstateSession.Screen.Dungeon)
            {
                // the session left the hub with the camp open (to the menu): the rest stop must not stay loaded
                Close();
                CampView.HideNow();
                return;
            }
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && (_camp.Armed != null || _restArmed)) Cancel();
            var mouse = Mouse.current;
            // a right click anywhere puts an armed skill away (DD1's way out of a choice)
            if (mouse != null && mouse.rightButton.wasPressedThisFrame && (_camp.Armed != null || _restArmed)) Cancel();
            Fade();
            // the seated heroes answer the pointer once the rest stop is in view, and until night falls
            CampHeroes.Tick(_nightSince < 0f && !_cover.gameObject.activeSelf && !_camp.Closing);
            if (Time.unscaledTime < _nextRefresh) return;
            Refresh();
        }

        private void Fade()
        {
            if (_nightSince >= 0f)
            {
                _cover.color = new Color(0f, 0f, 0f, Mathf.Max(_cover.color.a, Mathf.Clamp01((Time.unscaledTime - _nightSince) / FadeTime)));
                return;
            }
            if (!_cover.gameObject.activeSelf || !CampView.Ready) return;
            if (CampView.State != "shown")
            {
                // no rest stop to show: the dark stays above the HUD, and lets the HUD's panels take clicks
                ((RectTransform)_cover.transform).offsetMin = new Vector2(0f, PanelHeight);
                return;
            }
            if (_shownSince < 0f) _shownSince = Time.unscaledTime;
            var t = (Time.unscaledTime - _shownSince) / FadeTime;
            _cover.color = new Color(0f, 0f, 0f, Mathf.Clamp01(1f - t));
            if (t >= 1f) _cover.gameObject.SetActive(false);
        }

        // ---- refresh ---------------------------------------------------------------------------------

        private void Refresh()
        {
            _nextRefresh = Time.unscaledTime + 0.2f;
            if (_camp == null) return;
            if (_message != null && Time.unscaledTime > _messageUntil) _message = null;
            ShowScroll();
            ShowNote();
        }

        // One thing at a time: what went wrong, what is being asked, what to do next; under it the last of the
        // camp's log (what the meal and the skills did).
        private void ShowNote()
        {
            var session = _camp.Session;
            var hero = session.Hero(_camp.Selected);
            var text = new StringBuilder();
            if (_message != null)
                text.Append("<color=").Append(RaidText.Hex(_messageIsWarning ? UiKit.Harmful : UiKit.Notable)).Append('>').Append(_message).Append("</color>");
            else if (_camp.Armed != null)
                text.Append("<color=").Append(RaidText.Hex(UiKit.Notable)).Append('>').Append(CampText.Name(CampContent.Rules.Skill(_camp.Armed))).Append("</color>: click a companion. A right click puts it away.");
            else if (_camp.Closing) text.Append("The party sleeps.");
            else if (session.Phase == CampPhase.Meal) text.Append("The party eats first: choose a meal on the scroll. ").Append(_camp.FoodCarried).Append(" food is in the bag.");
            else if (hero != null && hero.Skills.Count == 0) text.Append(Camp.Name(hero.Id)).Append(" has no camping skills ready. The Survivalist in the hamlet teaches them.");
            else
                text.Append("Click a hero to see their camping skills in the banner, a skill to use it. ").Append(Mathf.RoundToInt((float)(session.AmbushChance * 100))).Append("% chance of an ambush in the night.");

            var lines = _camp.Log;
            if (lines.Count > 0) text.Append("\n<color=#").Append(ColorUtility.ToHtmlStringRGB(Color.Lerp(UiKit.Neutral, Color.black, 0.35f))).Append('>');
            for (var i = Mathf.Max(0, lines.Count - LogLines); i < lines.Count; i++) text.Append('\n').Append(lines[i]);
            if (lines.Count > 0) text.Append("</color>");

            var words = text.ToString();
            if (_note.text != words)
            {
                _note.text = words;
                var height = Mathf.Ceil(_note.GetPreferredValues(words, NoteWidth - 2f * NotePad, 0f).y);
                _noteBack.PlaceTopLeft(new Vector2(ScrollPos.x, ScrollPos.y + _scroll.sizeDelta.y + NoteGap), RaidUi.TopLeft, new Vector2(NoteWidth, height + 2f * NotePad));
                ((RectTransform)_note.transform).PlaceTopLeft(new Vector2(NotePad, NotePad), RaidUi.TopLeft, new Vector2(NoteWidth - 2f * NotePad, height));
            }
            _noteBack.gameObject.SetActive(true);
        }

        // The Repast scroll while the party has to eat, the Respite scroll after.
        private void ShowScroll()
        {
            var session = _camp.Session;
            var rules = CampContent.Rules;
            var key = session.Phase + "|" + session.Points + "|" + _camp.FoodCarried + "|" + _camp.Closing + "|" + session.Party.Count;
            if (key != _shownScroll)
            {
                _shownScroll = key;
                _tooltip.HideAll();
                RaidUi.Clear(_scroll);
                if (session.Phase == CampPhase.Meal) BuildMealScroll(rules);
                else BuildRespiteScroll();
                _note.text = null;      // the note moves with the scroll's height
            }
            _scroll.gameObject.SetActive(!_camp.Closing);
        }

        // scripts/layout/screen.raid.darkest, meal_scroll: the heading at headerY, the four rations at buttonY,
        // buttonOffset apart, each with a tooltip at tooltipOffset from its card.
        private void BuildMealScroll(CampingRules rules)
        {
            var l = RaidLayout.Current;
            _scroll.sizeDelta = MealScrollSize;
            RaidUi.Art("Art", _scroll, ScrollDir + "meal_scroll.png", Vector2.zero, MealScrollSize, new Color(0.1f, 0.08f, 0.05f, 0.98f), true);
            var title = RaidUi.Label("Title", _scroll, "scroll_header", new Vector2(MealScrollSize.x * 0.5f, l.MealHeaderY), new Vector2(HeaderWidth, 64f), TextAlignmentOptions.Top, UiKit.Notable);
            RaidUi.Fit(title, 24f);
            title.text = RaidText.Get("str_ui_meal_title", "Repast");

            var food = InventoryContent.Items.Food;
            var count = rules.Meals.Count;
            var left = (MealScrollSize.x - ((count - 1) * l.MealButtonOffset + l.ItemIconSize.x)) / 2f;
            for (var i = 0; i < count; i++)
            {
                var meal = rules.Meals[i];
                var type = meal.Type;
                var needed = _camp.Session.FoodFor(meal);
                var refusal = _camp.MealRefusal(type);
                var open = refusal == null;
                var at = new Vector2(left + i * l.MealButtonOffset, l.MealButtonY);
                // DD1's food card, fuller the bigger the meal; the first of them, darkened, for going without
                var sprite = food != null ? InventoryContent.Icon(food, food.StackLimit * i / Mathf.Max(1, count - 1)) : null;
                var image = UiKit.Image("Meal." + type, _scroll, sprite, sprite == null ? new Color(0.2f, 0.2f, 0.2f) : !open ? new Color(0.3f, 0.3f, 0.3f) : needed == 0 ? new Color(0.45f, 0.45f, 0.45f) : Color.white, true);
                ((RectTransform)image.transform).PlaceTopLeft(at, RaidUi.TopLeft, l.ItemIconSize);

                var amountAt = l.ItemAmount - l.ItemIconOffset;
                var amount = RaidUi.Label("Food." + type, image.transform, "inventory_amount", amountAt, new Vector2(l.ItemIconSize.x - amountAt.x, 40f), TextAlignmentOptions.TopLeft, open ? (Color?)null : UiKit.Harmful);
                amount.text = needed.ToString();
                RaidUi.Shadow(amount).text = needed.ToString();
                var name = RaidUi.Label("Name." + type, image.transform, "tooltip", new Vector2(l.ItemIconSize.x * 0.5f, l.ItemIconSize.y - MealNameRise), new Vector2(l.ItemIconSize.x, 26f), TextAlignmentOptions.Top, open ? UiKit.Neutral : new Color(0.45f, 0.43f, 0.4f));
                RaidUi.Fit(name, 12f);
                name.text = CampText.MealName(meal);

                var words = RaidTooltip.Titled(CampText.MealName(meal), needed + " food\n" + CampText.MealEffect(meal) + " for every hero"
                                                                        + (refusal != null ? "\n<color=" + RaidText.Hex(UiKit.Harmful) + ">" + refusal + "</color>" : ""));
                RaidUi.Pointer(image, () => OnMeal(type), null, inside =>
                {
                    // DD1 hangs the tooltip to the right of the card (tooltipOffset); here the scroll is at the
                    // screen's right edge, so it hangs to the left of the scroll, at the same height
                    if (inside) _tooltip.Show(image, words, new Vector2(ScrollPos.x - l.MealTooltipWidth - MealTooltipGap, ScrollPos.y + at.y + l.MealTooltipOffset.y), l.MealTooltipWidth);
                    else _tooltip.Hide(image);
                });
            }
        }

        // scripts/layout/screen.raid.darkest, camp_layout: the title at respite_title_offset, the points ending
        // at respite_points_offset (before the hourglass the scroll's art has at its right), the text at
        // respite_description_offset, the REST plate at respite_rest_offset with its word at
        // respite_rest_text_offset.
        private void BuildRespiteScroll()
        {
            var l = RaidLayout.Current;
            _scroll.sizeDelta = RespiteScrollSize;
            RaidUi.Art("Art", _scroll, ScrollDir + "event_scroll_campingrespite.png", Vector2.zero, RespiteScrollSize, new Color(0.1f, 0.08f, 0.05f, 0.98f), true);
            var title = RaidUi.Label("Title", _scroll, "scroll_header", l.RespiteTitle, new Vector2(l.RespitePoints.x - l.RespiteTitle.x - 70f, 64f), TextAlignmentOptions.TopLeft, UiKit.Notable);
            RaidUi.Fit(title, 24f);
            title.text = RaidText.Get("camping_respite_title", "Respite");
            RaidUi.Label("Points", _scroll, "camping_points", l.RespitePoints, new Vector2(90f, 64f), TextAlignmentOptions.TopRight, UiKit.Neutral).text = _camp.Session.Points.ToString();
            var description = RaidUi.Paragraph("Description", _scroll, "scroll_body", l.RespiteDescription, new Vector2(l.RespiteDescriptionWidth, l.RespiteRest.y - l.RespiteDescription.y - 4f), TextAlignmentOptions.TopLeft, UiKit.Neutral);
            description.text = RaidText.Get("camping_respite_description", "Use Camping skills to bolster your party's physical and mental health.");

            var rest = RaidUi.ArtButton("Rest", _scroll, "overlays/announcement_rest.png", l.RespiteRest, OnRest, RestArt);
            rest.interactable = !_camp.Closing;
            var word = RaidUi.Label("Word", rest.transform, "rest", l.RespiteRestText, new Vector2(RestArt.x, 44f), TextAlignmentOptions.Top, UiKit.Notable);
            word.text = RaidText.Get("camping_respite_rest", "REST");
        }
    }
}
