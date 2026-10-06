using System;
using System.Collections.Generic;
using Assets.Code.Audio;
using Assets.Code.CommonLogic.Pooling;
using Assets.Code.UI;
using Assets.Code.UI.Managers;
using Assets.Code.UI.Options;
using Assets.Code.UI.Screens;
using Assets.Code.UI.Utilities;
using Assets.Code.UI.Widgets;
using Assets.Code.Utils;
using DD2Estate.Dd2;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace DD2Estate.Menu
{
    /// <summary>
    /// One more tab, "Estate", in DD2's own options screen while an Estate session runs (the pause menu's
    /// Options in the hamlet, in a dungeon, in a fight). Outside a session the screen is the game's own: the
    /// tab is put into the INSTANCE the game makes of its options prefab each time the screen opens (the
    /// instance is destroyed when it closes), never into the prefab.
    ///
    /// The game's screen (Assets.Code.UI.Screens.OptionsMenuUiBhv) keeps a list of tabs, each a button of the
    /// list on the left, a page on the right and the layout its rows stand in. The Estate's is added to that
    /// list before the game wires the list up (a prefix on OnScreenPushed), so the game itself gives the
    /// button its click, shows and hides the page with the others and builds the up / down navigation of the
    /// list and of the page's rows.
    ///
    /// Everything seen is a copy of a part of the game's, taken from the screen that is opening:
    ///   the tab's button     the Game tab's button
    ///   the page             the Game tab's page: its ink, its title and rule, its scroll view and bar
    ///   a choice of two      the Game page's Language row (a heading over a dropdown)
    ///   a switch             the row the game's pool copies for a switch (OptionsToggle)
    ///   a level              the row its pool copies for a slider (OptionsSlider)
    ///   the folder           the Language row's heading, the Audio page's device name for the two lines of
    ///                        text, the Graphics page's Update button for "Change" and "Find through Steam";
    ///                        the folder is typed into the game's own dialog for a text (NameInputDialogBhv)
    ///   what a row does      the game's option tooltip (a row's TooltipCanvasElement), shown as the game
    ///                        shows it: when the pointer is over the row's name
    /// Copies are made inside a holder that is off, the game's behaviours that would fill them with the
    /// game's own options are destroyed before they wake (OptionsItemBhv, the data context and its texts,
    /// the localizers, the language widget), and what a control calls is replaced.
    ///
    /// Dev bridge: see <see cref="EstateOptionsDev"/>.
    /// </summary>
    internal static class EstateOptionsTab
    {
        /// <summary>The tab's own group beside the game's (AUDIO, GRAPHICS, GAME, CONTROL, DEBUG, ALTAR are 0 to 5); no option of the game's is of it.</summary>
        internal const OptionsValue.OptionGroup Group = (OptionsValue.OptionGroup)57;

        private const string Title = "Estate";
        private const float FolderWidth = 430f;         // what the page's scroll view shows of a row
        private const float ButtonHeight = 35.7f;       // the Update button's own
        private const float MinTabSpacing = 12f;
        private const float ClearOfVersion = 6f;
        private const int FolderLimit = 260;

        internal sealed class Row
        {
            public EstateOptions.Setting Setting;
            public GameObject Object, Tip;
            public TMP_Dropdown Dropdown;
            public Toggle Toggle;
            public Slider Slider;
            public TMP_Text Folder, Status;
            public Button Change, Steam;

            public Selectable First => Dropdown != null ? Dropdown : Toggle != null ? Toggle : Slider != null ? (Selectable)Slider : Change;
        }

        internal sealed class Tab
        {
            public OptionsMenuUiBhv Screen;
            public OptionsMenuUiBhv.OptionsTab Entry;
            public int Index;
            public ScrollRect Scroll;
            public VerticalLayoutGroup List;
            public float ListSpacingWas;
            public Color StatusColour = Color.grey;
            /// <summary>The diamond before the tab's name in the list.</summary>
            public Image Diamond;
            public readonly List<Row> Rows = new List<Row>();
        }

        private static Tab _tab;

        /// <summary>The tab of the options screen that is up; null when none is, or it has none (outside a session).</summary>
        internal static Tab Live => _tab != null && _tab.Screen != null && _tab.Entry?.m_page != null ? _tab : null;

        internal static List<OptionsMenuUiBhv.OptionsTab> TabsOf(OptionsMenuUiBhv screen)
        {
            return screen == null ? null : Traverse.Create(screen).Field("m_tabs").GetValue<List<OptionsMenuUiBhv.OptionsTab>>();
        }

        // ---- the tab -------------------------------------------------------------------------------------

        /// <summary>Puts the tab into an options screen that is about to be wired up. Throws when the screen is not what it was (a game update): the screen is then left as the game made it.</summary>
        internal static void Add(OptionsMenuUiBhv screen)
        {
            var tabs = TabsOf(screen);
            if (tabs == null) throw new InvalidOperationException("the options screen has no list of tabs");
            if (tabs.Exists(tab => tab != null && tab.m_group == Group)) return;
            var game = tabs.Find(tab => tab != null && tab.m_group == OptionsValue.OptionGroup.GAME);
            if (game?.m_button == null || game.m_page == null || game.m_layout == null) throw new InvalidOperationException("the options screen has no Game tab to copy");
            var buttons = Traverse.Create(screen).Field("m_tabsParent").GetValue<Transform>() ?? game.m_button.transform.parent;

            var holder = new GameObject("DD2Estate.OptionsParts");
            holder.SetActive(false);
            holder.transform.SetParent(screen.transform, false);
            try
            {
                var made = new Tab { Screen = screen };

                // the button of the list
                var button = Object.Instantiate(game.m_button.gameObject, holder.transform, false);
                button.name = "EstateTabButton";
                Strip(button);
                foreach (var label in button.GetComponentsInChildren<TMP_Text>(true)) label.text = Title;
                var click = button.GetComponent<Button>();
                if (click == null) throw new InvalidOperationException("the Game tab's button is no Button");
                click.onClick = new Button.ButtonClickedEvent();
                var highlight = Traverse.Create(button.GetComponent<OptionsTabBtnBhv>()).Field("m_highlightCanvasGroup").GetValue<CanvasGroup>();
                if (highlight != null) highlight.alpha = 0f;
                made.Diamond = Diamond(button, highlight);
                if (made.Diamond != null) made.Diamond.color = DiamondColour(made.Diamond.color);
                else Plugin.Log.LogWarning("options: the Estate entry has no diamond to paint (the list's entries are not what they were)");

                // the page
                var page = Object.Instantiate(game.m_page, holder.transform, false);
                page.name = "OptionsTab_Estate";
                var layout = Counterpart(game.m_page.transform, game.m_layout, page.transform) as RectTransform;
                if (layout == null) throw new InvalidOperationException("the copy of the Game page has no layout");
                made.Scroll = layout.GetComponentInParent<ScrollRect>(true);
                foreach (var label in page.GetComponentsInChildren<TMP_Text>(true))
                {
                    // the page's own title: the one text that is localized and is not a row's
                    if (label.transform.IsChildOf(layout) || !HasBehaviour(label.gameObject, "LocalizeTextBhv")) continue;
                    Strip(label.gameObject);
                    label.text = Title;
                }

                // the game's rows of this page go; one with a dropdown is kept to copy
                var dropdown = layout.GetComponentInChildren<TMP_Dropdown>(true);
                GameObject choiceRow = null;
                if (dropdown != null)
                {
                    var row = dropdown.transform;
                    while (row.parent != layout) row = row.parent;
                    row.SetParent(holder.transform, false);
                    choiceRow = row.gameObject;
                }
                for (var i = layout.childCount - 1; i >= 0; i--) Object.DestroyImmediate(layout.GetChild(i).gameObject);

                var switchRow = Traverse.Create(screen).Field("m_optionsTogglePool").GetValue<GameObjectPoolBhv>()?.prefab;
                var levelRow = Traverse.Create(screen).Field("m_optionsSliderPool").GetValue<GameObjectPoolBhv>()?.prefab;
                var audio = tabs.Find(tab => tab != null && tab.m_group == OptionsValue.OptionGroup.AUDIO);
                var words = FindText(audio?.m_page, "DeviceName");
                var update = Traverse.Create(screen.GetComponentInChildren<ScreenResolutionWidgetBhv>(true)).Field("m_resolutionUpdateButton").GetValue<Button>();

                // A row that cannot be made (a part of the game's is gone) is left out and said in the log; the others stand.
                GameObject tip = null;
                var rows = new Dictionary<string, Row>();
                foreach (var setting in EstateOptions.All)
                {
                    if (setting.Kind != EstateOptions.Kind.Switch && setting.Kind != EstateOptions.Kind.Level) continue;
                    try
                    {
                        var row = setting.Kind == EstateOptions.Kind.Switch ? SwitchRow(made, setting, switchRow, holder.transform) : LevelRow(made, setting, levelRow, holder.transform);
                        rows[setting.Id] = row;
                        if (tip == null && setting.Kind == EstateOptions.Kind.Switch) tip = row.Tip;
                    }
                    catch (Exception e) { Plugin.Log.LogError("options: the row '" + setting.Id + "' could not be made: " + e.Message); }
                }
                foreach (var setting in EstateOptions.All)
                {
                    if (setting.Kind != EstateOptions.Kind.Choice && setting.Kind != EstateOptions.Kind.Folder) continue;
                    try
                    {
                        rows[setting.Id] = setting.Kind == EstateOptions.Kind.Choice
                            ? ChoiceRow(made, setting, choiceRow, holder.transform, tip)
                            : FolderRow(made, setting, choiceRow, words, update, holder.transform, tip);
                    }
                    catch (Exception e) { Plugin.Log.LogError("options: the row '" + setting.Id + "' could not be made: " + e.Message); }
                }
                if (rows.Count == 0) throw new InvalidOperationException("no row could be made");
                foreach (var setting in EstateOptions.All)
                {
                    if (!rows.TryGetValue(setting.Id, out var row)) continue;
                    row.Object.transform.SetParent(layout, false);
                    row.Object.SetActive(true);
                    made.Rows.Add(row);
                }

                // all made: into the game's screen
                page.SetActive(false);
                page.transform.SetParent(game.m_page.transform.parent, false);
                page.transform.SetSiblingIndex(game.m_page.transform.GetSiblingIndex() + 1);
                button.transform.SetParent(buttons, false);
                button.transform.SetSiblingIndex(game.m_button.transform.GetSiblingIndex() + 1);
                button.SetActive(true);
                made.Entry = new OptionsMenuUiBhv.OptionsTab { m_group = Group, m_button = click, m_page = page, m_layout = layout, m_highlightCanvasGroup = highlight };
                tabs.Add(made.Entry);
                made.Index = tabs.Count - 1;
                _tab = made;
                try { FitList(made, buttons); }
                catch (Exception e) { Plugin.Log.LogWarning("options: the list of tabs was not fitted: " + e.Message); }
                Refresh();
                Plugin.Log.LogInfo("options: the Estate tab is in the options screen (" + made.Rows.Count + " rows)");
            }
            finally
            {
                Object.Destroy(holder);
            }
        }

        /// <summary>The options screen closed: what waits to be written is written, a refusal is forgotten.</summary>
        internal static void Closed(OptionsMenuUiBhv screen)
        {
            EstateOptions.Flush();
            EstateOptions.ForgetFolderProblem();
            if (_tab != null && (_tab.Screen == null || _tab.Screen == screen)) _tab = null;
        }

        /// <summary>The game has shown a tab's page and built its navigation: the Estate's own is brought up to date.</summary>
        internal static void Shown(OptionsMenuUiBhv screen, int index)
        {
            var tab = Live;
            if (tab == null || tab.Screen != screen || index != tab.Index) return;
            Refresh();
            // the game links a row's first control up and down; the folder's second button hangs beside the first
            foreach (var row in tab.Rows)
            {
                if (row.Change == null || row.Steam == null) continue;
                var first = row.Change.navigation;
                first.mode = Navigation.Mode.Explicit;
                first.selectOnRight = row.Steam;
                row.Change.navigation = first;
                row.Steam.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = first.selectOnUp, selectOnDown = first.selectOnDown, selectOnLeft = row.Change };
            }
        }

        // ---- the entry's diamond ---------------------------------------------------------------------------
        // Every entry of the list has a small diamond before its name: one white picture (ui_diamond_white),
        // tinted grey (#757575) for the tabs and gold (#B18D4D) for Feedback and Credits. The game draws an
        // entry's three states with the glow behind it (Highlight: off, half and whole for idle, pointed at and
        // chosen) and leaves the diamond and the name as they are, so the Estate's diamond is one red in all
        // three. (The owner, 2026-10-06, of the Estate's entry: "make the marker red".)

        private const string DiamondSprite = "ui_diamond_white";

        /// <summary>The Estate entry's diamond is this red: the mod's colour of harm (#B11900), DD1's own.</summary>
        internal static Color DiamondColour(Color was)
        {
            var red = DD2Estate.Estate.SheetColours.Harm;
            return new Color(red.r, red.g, red.b, was.a);
        }

        // The diamond of an entry: the picture of that name, else the entry's one picture that is not its own
        // ground and not of its glow.
        private static Image Diamond(GameObject entry, CanvasGroup highlight)
        {
            Image other = null;
            var others = 0;
            foreach (var image in entry.GetComponentsInChildren<Image>(true))
            {
                if (image.gameObject == entry || (highlight != null && image.transform.IsChildOf(highlight.transform))) continue;
                if (image.sprite != null && image.sprite.name == DiamondSprite) return image;
                other = image;
                others++;
            }
            return others == 1 ? other : null;
        }

        // The object of a copy that stands where an object stands in what was copied.
        private static Transform Counterpart(Transform root, Transform part, Transform copy)
        {
            var path = new List<int>();
            for (var t = part; t != root; t = t.parent)
            {
                if (t == null) return null;
                path.Add(t.GetSiblingIndex());
            }
            var at = copy;
            for (var i = path.Count - 1; i >= 0; i--)
            {
                if (path[i] >= at.childCount) return null;
                at = at.GetChild(path[i]);
            }
            return at;
        }

        private static TMP_Text FindText(GameObject under, string name)
        {
            if (under == null) return null;
            foreach (var text in under.GetComponentsInChildren<TMP_Text>(true))
                if (text.name == name) return text;
            return null;
        }

        private static bool HasBehaviour(GameObject go, string type)
        {
            foreach (var behaviour in go.GetComponents<MonoBehaviour>())
                if (behaviour != null && behaviour.GetType().Name == type) return true;
            return false;
        }

        // The game's behaviours a copy keeps: what makes it look, sound and move as the game's rows do. All
        // others would fill it with an option of the game's, a word of its string table, a language list.
        private static readonly HashSet<string> Kept = new HashSet<string>
        {
            "UIHoverStateBhv", "SelectableFunctionBhv", "ScrollToCenterBhv", "ToggleAudioBhv", "ButtonAudioBhv", "ContentSizeFitterWithMax", "UiImagePropertiesBhv", "OptionsTabBtnBhv"
        };

        /// <summary>Destroys the game's own behaviours on a copy that has not woken yet, all but <see cref="Kept"/>; Unity's and TextMeshPro's stay.</summary>
        internal static void Strip(GameObject root)
        {
            for (var pass = 0; pass < 3; pass++)
            {
                var left = 0;
                foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (behaviour == null) continue;
                    var type = behaviour.GetType();
                    var space = type.Namespace ?? "";
                    if (space.StartsWith("UnityEngine", StringComparison.Ordinal) || space.StartsWith("TMPro", StringComparison.Ordinal) || Kept.Contains(type.Name)) continue;
                    Object.DestroyImmediate(behaviour);
                    if (behaviour != null) left++;
                }
                if (left == 0) return;
            }
        }

        // ---- rows ----------------------------------------------------------------------------------------

        private static Color Positive => SingletonMonoBehaviour<CommonUiBhv>.HasInstance() ? SingletonMonoBehaviour<CommonUiBhv>.Instance.PositiveColour : new Color(0.69f, 0.58f, 0.37f);
        // A refusal is red, the Estate's colour of harm (DD2's own "negative" colour is a blue).
        private static Color Refused => DD2Estate.Estate.SheetColours.Harm;

        // A copy of a pooled row of the game's, bare: its name, its tooltip, the colour the game gives a row's name.
        private static Row PooledRow(Tab tab, EstateOptions.Setting setting, GameObject template, Transform holder)
        {
            if (template == null) throw new InvalidOperationException("the options screen has no row of that kind to copy");
            var go = Object.Instantiate(template, holder, false);
            go.name = "Estate_" + setting.Id;
            var item = go.GetComponent<OptionsItemBhv>();
            var name = item != null ? Traverse.Create(item).Field("m_optionNameLabel").GetValue<TextMeshProUGUI>() : null;
            var tip = item != null ? Traverse.Create(item).Field("m_tooltipObject").GetValue<GameObject>() : null;
            var colour = item != null ? Traverse.Create(item).Field("m_defaultColour").GetValue<Color>() : Color.grey;
            Strip(go);
            if (name == null) throw new InvalidOperationException("the row has no name label");
            name.text = setting.Label;
            name.color = colour;
            tab.StatusColour = colour;
            var row = new Row { Setting = setting, Object = go, Tip = tip };
            if (tip != null)
            {
                // at the left foot of the row's name, where the game's switch has it (its slider, which never
                // shows one, has it further to the right)
                var at = (RectTransform)tip.transform;
                at.anchoredPosition = new Vector2(-((RectTransform)name.transform).rect.width * 0.5f, at.anchoredPosition.y);
                Describe(name.gameObject, tip, setting.Tip);
            }
            foreach (var centre in go.GetComponentsInChildren<ScrollToCenterBhv>(true))
                if (!centre.HasScrollRect && tab.Scroll != null) centre.SetScrollRect(tab.Scroll);
            return row;
        }

        private static Row SwitchRow(Tab tab, EstateOptions.Setting setting, GameObject template, Transform holder)
        {
            var row = PooledRow(tab, setting, template, holder);
            row.Toggle = row.Object.GetComponentInChildren<Toggle>(true);
            if (row.Toggle == null) throw new InvalidOperationException("the row has no switch");
            row.Toggle.onValueChanged = new Toggle.ToggleEvent();
            row.Toggle.interactable = true;
            // as OptionsItemBhv.SetUnlocked colours the check mark of a row that can be used
            var mark = row.Toggle.GetComponent<Image>();
            if (mark != null) mark.color = Positive;
            row.Toggle.onValueChanged.AddListener(on => Changed(setting, on));
            return row;
        }

        private static Row LevelRow(Tab tab, EstateOptions.Setting setting, GameObject template, Transform holder)
        {
            var row = PooledRow(tab, setting, template, holder);
            row.Slider = row.Object.GetComponentInChildren<Slider>(true);
            if (row.Slider == null) throw new InvalidOperationException("the row has no slider");
            row.Slider.onValueChanged = new Slider.SliderEvent();
            row.Slider.interactable = true;
            row.Slider.wholeNumbers = false;
            row.Slider.minValue = 0f;
            row.Slider.maxValue = 1f;
            row.Slider.onValueChanged.AddListener(level => Changed(setting, level));
            return row;
        }

        private static Row ChoiceRow(Tab tab, EstateOptions.Setting setting, GameObject template, Transform holder, GameObject tip)
        {
            if (template == null) throw new InvalidOperationException("the Game page has no row with a dropdown to copy");
            var go = Object.Instantiate(template, holder, false);
            go.name = "Estate_" + setting.Id;
            Strip(go);
            var row = new Row { Setting = setting, Object = go, Dropdown = go.GetComponentInChildren<TMP_Dropdown>(true) };
            if (row.Dropdown == null) throw new InvalidOperationException("the row has no dropdown");
            var heading = Heading(go, row.Dropdown.transform);
            if (heading == null) throw new InvalidOperationException("the row has no heading");
            heading.text = setting.Label;
            row.Dropdown.onValueChanged = new TMP_Dropdown.DropdownEvent();
            row.Dropdown.interactable = true;
            row.Dropdown.ClearOptions();
            row.Dropdown.AddOptions(new List<string>(setting.Words));
            row.Dropdown.onValueChanged.AddListener(index => Changed(setting, index));
            row.Tip = TipUnder(heading, tip, setting.Tip);
            return row;
        }

        // The heading of a row: its first text that is not part of the control.
        private static TMP_Text Heading(GameObject row, Transform control)
        {
            foreach (var text in row.GetComponentsInChildren<TMP_Text>(true))
                if (!text.transform.IsChildOf(control)) return text;
            return null;
        }

        // A copy of the game's option tooltip under a heading, at the heading's left foot.
        private static GameObject TipUnder(TMP_Text heading, GameObject template, string words)
        {
            if (template == null || heading == null) return null;
            var tip = Object.Instantiate(template, heading.transform, false);
            tip.name = template.name;
            tip.SetActive(false);
            var rect = (RectTransform)tip.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(0f, -4f);
            heading.raycastTarget = true;
            Describe(heading.gameObject, tip, words);
            return tip;
        }

        // What a row does, the game's way: its tooltip comes while the pointer is over the row's name (the
        // game's hover behaviour shows the object it is given; OptionsItemBhv, which did it for the game's
        // rows, is gone from the copy) with the game's sound of a pointer coming over something.
        private static void Describe(GameObject name, GameObject tip, string words)
        {
            foreach (var text in tip.GetComponentsInChildren<TMP_Text>(true)) text.text = words;
            tip.SetActive(false);
            var hover = name.GetComponent<UIHoverStateBhv>();
            if (hover == null) hover = name.AddComponent<UIHoverStateBhv>();
            var fields = Traverse.Create(hover);
            fields.Field("m_showOnHover").SetValue(tip);
            var begin = new UnityEvent();
            begin.AddListener(() =>
            {
                try { SingletonMonoBehaviour<AudioMgr>.Instance.Play(AudioPathsBhv.Mouseover); }
                catch (Exception) { }
            });
            fields.Field("m_hoverBeginEvent").SetValue(begin);
            fields.Field("m_hoverEndEvent").SetValue(new UnityEvent());
        }

        private static Row FolderRow(Tab tab, EstateOptions.Setting setting, GameObject headingRow, TMP_Text words, Button button, Transform holder, GameObject tip)
        {
            if (headingRow == null || words == null || button == null) throw new InvalidOperationException("a part to copy is missing (" + (headingRow == null ? "a heading" : words == null ? "the Audio page's device name" : "the Graphics page's Update button") + ")");
            var headingSource = Heading(headingRow, headingRow.GetComponentInChildren<TMP_Dropdown>(true).transform);
            if (headingSource == null) throw new InvalidOperationException("the row to copy has no heading");

            // the row's own frame: a column as wide as the page shows, as tall as what stands in it
            var go = new GameObject("Estate_" + setting.Id, typeof(RectTransform));
            go.transform.SetParent(holder, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(FolderWidth, 150f);
            var column = go.AddComponent<VerticalLayoutGroup>();
            column.childAlignment = TextAnchor.UpperLeft;
            column.spacing = 4f;
            column.childControlWidth = column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;
            var fitter = go.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var row = new Row { Setting = setting, Object = go };

            var heading = Object.Instantiate(headingSource.gameObject, go.transform, false).GetComponent<TMP_Text>();
            heading.name = "Title";
            Strip(heading.gameObject);
            heading.text = setting.Label;
            heading.textWrappingMode = TextWrappingModes.NoWrap;
            heading.gameObject.AddComponent<LayoutElement>().minHeight = ((RectTransform)headingSource.transform).rect.height;
            heading.gameObject.SetActive(true);
            row.Tip = TipUnder(heading, tip, setting.Tip);

            row.Folder = Line(words, go.transform, "Folder", words.color);
            row.Status = Line(words, go.transform, "Status", tab.StatusColour);

            var bar = new GameObject("Buttons", typeof(RectTransform));
            bar.transform.SetParent(go.transform, false);
            var side = bar.AddComponent<HorizontalLayoutGroup>();
            side.childAlignment = TextAnchor.LowerLeft;
            side.spacing = 12f;
            side.childControlWidth = side.childControlHeight = false;
            side.childForceExpandWidth = side.childForceExpandHeight = false;
            bar.AddComponent<LayoutElement>().minHeight = ButtonHeight + 10f;
            row.Change = TextButton(tab, button, bar.transform, "Change", 150f, go.transform, AskFolder);
            row.Steam = TextButton(tab, button, bar.transform, "Find through Steam", 250f, go.transform, () =>
            {
                var problem = EstateOptions.FindThroughSteam();
                Sound(problem == null);
                Refresh();
            });
            return row;
        }

        // A line of plain text in the look of the Audio page's device name.
        private static TMP_Text Line(TMP_Text source, Transform parent, string name, Color colour)
        {
            var line = Object.Instantiate(source.gameObject, parent, false).GetComponent<TMP_Text>();
            line.name = name;
            Strip(line.gameObject);
            line.text = "";
            line.color = colour;
            line.alignment = TextAlignmentOptions.TopLeft;
            line.textWrappingMode = TextWrappingModes.Normal;
            line.overflowMode = TextOverflowModes.Overflow;
            line.raycastTarget = false;
            line.gameObject.SetActive(true);
            return line;
        }

        // A copy of the Graphics page's Update button with other words and another errand.
        private static Button TextButton(Tab tab, Button source, Transform parent, string words, float width, Transform row, Action pressed)
        {
            var go = Object.Instantiate(source.gameObject, parent, false);
            go.name = words.Replace(" ", "") + "Button";
            Strip(go);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(width, rect.sizeDelta.y > 1f ? rect.sizeDelta.y : ButtonHeight);
            foreach (var label in go.GetComponentsInChildren<TMP_Text>(true))
            {
                label.enableAutoSizing = false;
                label.text = words;
            }
            var button = go.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            button.interactable = true;     // the game's own waits for another resolution to be picked
            button.onClick.AddListener(() =>
            {
                try { pressed(); }
                catch (Exception e) { Plugin.Log.LogError("options: " + words + ": " + e); }
            });
            // with a controller: the game's pointer comes to the row and the page scrolls to it, as for its own rows
            var screen = tab.Screen;
            var select = new UnityEvent();
            select.AddListener(() =>
            {
                if (screen != null && row != null) screen.OnOptionSelected(row);
            });
            Traverse.Create(go.AddComponent<SelectableFunctionBhv>()).Field("m_selectEventBhv").SetValue(select);
            if (tab.Scroll != null)
            {
                var centre = go.AddComponent<ScrollToCenterBhv>();
                centre.SetScrollRect(tab.Scroll);
                Traverse.Create(centre).Field("m_onlyScrollOnController").SetValue(true);
            }
            go.SetActive(true);
            return button;
        }

        // ---- the list on the left ---------------------------------------------------------------------------

        // One entry more in the list of tabs. With the game's four it has room; with the game's debug entries on
        // (a development set-up) the list would run into the version line at the screen's foot: then the list's
        // spacing is taken in by just what it lacks.
        private static void FitList(Tab tab, Transform buttons)
        {
            tab.List = buttons.GetComponent<VerticalLayoutGroup>();
            var version = tab.Screen.transform.Find("VersionText") as RectTransform;
            if (tab.List == null || version == null) return;
            tab.ListSpacingWas = tab.List.spacing;
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)buttons);
            RectTransform last = null;
            var entries = 0;
            foreach (RectTransform child in buttons)
            {
                if (!child.gameObject.activeSelf) continue;
                var element = child.GetComponent<LayoutElement>();
                if (element != null && element.ignoreLayout) continue;
                last = child;
                entries++;
            }
            if (last == null || entries < 2) return;
            var corners = new Vector3[4];
            last.GetWorldCorners(corners);
            var foot = buttons.InverseTransformPoint(corners[0]).y;
            version.GetWorldCorners(corners);
            var head = buttons.InverseTransformPoint(corners[1]).y;
            var over = head + ClearOfVersion - foot;
            if (over <= 0f) return;
            tab.List.spacing = Mathf.Max(MinTabSpacing, tab.List.spacing - over / (entries - 1));
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)buttons);
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            Plugin.Log.LogInfo("options: the list of tabs is " + entries + " long; its spacing is " + tab.List.spacing.ToString("0.#", culture) + " for " + tab.ListSpacingWas.ToString("0.#", culture) + " to stay clear of the version line");
        }

        // ---- changes -------------------------------------------------------------------------------------

        private static void Changed(EstateOptions.Setting setting, object value)
        {
            try
            {
                var problem = EstateOptions.Set(setting, value);
                if (problem == null) return;
                Plugin.Log.LogWarning("options: " + setting.Id + " was not changed: " + problem);
                Refresh();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("options: " + setting.Id + ": " + e);
            }
        }

        /// <summary>Every control of the tab shows what its setting is (nothing is told to anybody).</summary>
        internal static void Refresh()
        {
            var tab = Live;
            if (tab == null) return;
            foreach (var row in tab.Rows)
            {
                try
                {
                    var value = EstateOptions.Get(row.Setting);
                    if (row.Dropdown != null)
                    {
                        row.Dropdown.SetValueWithoutNotify((int)value);
                        row.Dropdown.RefreshShownValue();
                    }
                    if (row.Toggle != null) row.Toggle.SetIsOnWithoutNotify((bool)value);
                    if (row.Slider != null) row.Slider.SetValueWithoutNotify((float)value);
                    if (row.Folder != null)
                    {
                        var state = EstateOptions.Folder();
                        var folder = Breakable(state.Folder);
                        var status = Breakable(state.Status);
                        var changed = row.Folder.text != folder || row.Status.text != status;
                        row.Folder.text = folder;
                        row.Status.text = status;
                        row.Status.color = state.Bad ? Refused : tab.StatusColour;
                        if (row.Object.activeInHierarchy) LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)row.Object.transform);
                        if (tab.Entry.m_layout != null && tab.Entry.m_page.activeInHierarchy)
                        {
                            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)tab.Entry.m_layout);
                            // the row is the page's last and has grown or shrunk: the page shows it whole, its words and its buttons
                            if (changed && tab.Scroll != null && row.Object.transform.GetSiblingIndex() == row.Object.transform.parent.childCount - 1)
                            {
                                Canvas.ForceUpdateCanvases();
                                tab.Scroll.verticalNormalizedPosition = 0f;
                            }
                        }
                    }
                }
                catch (Exception e) { Plugin.Log.LogError("options: the row '" + row.Setting.Id + "' could not be brought up to date: " + e.Message); }
            }
        }

        private const string Break = "​";

        /// <summary>
        /// A path with a place to break the line after every separator (a zero-width space): left alone a path is
        /// one long word, cut wherever the row ends ("DarkestDunge" / "on").
        /// </summary>
        internal static string Breakable(string text)
        {
            return string.IsNullOrEmpty(text) ? text : text.Replace("\\", "\\" + Break).Replace("/", "/" + Break);
        }

        /// <summary>The words without the places to break.</summary>
        internal static string Plain(string text) => string.IsNullOrEmpty(text) ? text : text.Replace(Break, "");

        private static void Sound(bool good)
        {
            try { SingletonMonoBehaviour<AudioMgr>.Instance.Play(good ? AudioPathsBhv.ClickConfirm : AudioPathsBhv.ClickInvalid); }
            catch (Exception) { }
        }

        // ---- the folder's dialog ---------------------------------------------------------------------------

        /// <summary>
        /// "Change": the game's own dialog for a text (the one it names things with) asks for the folder. A path
        /// is set there in the game's text face and smaller than a name: a name is a few letters in the heading
        /// face, which has no backslash of its own. The instance is the dialog's own for this once.
        /// </summary>
        internal static void AskFolder()
        {
            var common = SingletonMonoBehaviour<CommonUiBhv>.Instance;
            var state = EstateOptions.Folder();
            var start = state.Config.Length > 0 ? state.Config : state.InUse ?? "";
            common.ShowNameInputDialog(EstateOptions.Find(EstateOptions.FolderId).Label, start, FolderLimit, FolderTyped, "Apply", typed => { }, "Cancel", ScreenStackBhv.Layer.PauseModal);
            try
            {
                var input = DialogInput(out _);
                if (input == null) return;
                input.richText = false;
                var face = Live?.Rows.Find(row => row.Folder != null)?.Folder.font;
                if (face != null) input.fontAsset = face;
                input.pointSize = 30f;
                if (input.textComponent != null)
                {
                    input.textComponent.richText = false;
                    // a path's "\n" and "\t" are a backslash and a letter, not a new line and a tab (seen in the
                    // main menu's question: "...\DarkestDungeon\no such folder" stood on two lines, its "\n" gone)
                    input.textComponent.parseCtrlCharacters = false;
                    input.textComponent.fontSize = 30f;
                }
                input.text = start;
            }
            catch (Exception e) { Plugin.Log.LogWarning("options: the folder's dialog keeps the look of a name's: " + e.Message); }
        }

        /// <summary>The dialog that is up and its input field; null when none is.</summary>
        internal static TMP_InputField DialogInput(out NameInputDialogBhv dialog)
        {
            dialog = null;
            if (!SingletonMonoBehaviour<CommonUiBhv>.HasInstance()) return null;
            var prefab = Traverse.Create(SingletonMonoBehaviour<CommonUiBhv>.Instance).Field("m_stringInputDialogPrefab").GetValue<GameObject>();
            var screen = CommonUiBhv.FindScreenInstance<UiScreenBhv>(prefab);
            if (screen == null) return null;
            dialog = screen.GetComponentInChildren<NameInputDialogBhv>(true);
            return dialog != null ? Traverse.Create(dialog).Field("m_InputField").GetValue<TMP_InputField>() : null;
        }

        internal static void FolderTyped(string typed)
        {
            var problem = EstateOptions.SetFolder(typed);
            Sound(problem == null);
            Refresh();
        }
    }

    /// <summary>The Estate's tab goes into the options screen the game has just made, while a session runs, before the game wires its tabs up.</summary>
    [HarmonyPatch(typeof(OptionsMenuUiBhv), nameof(OptionsMenuUiBhv.OnScreenPushed))]
    internal static class EstateOptionsTabAdded
    {
        private static void Prefix(OptionsMenuUiBhv __instance)
        {
            if (!EstateSession.Active) return;
            try { EstateOptionsTab.Add(__instance); }
            catch (Exception e) { Plugin.Log.LogError("options: the Estate tab was not added, the screen is the game's own: " + e); }
        }
    }

    [HarmonyPatch(typeof(OptionsMenuUiBhv), "HandleTabButtonClick")]
    internal static class EstateOptionsTabShown
    {
        private static void Postfix(OptionsMenuUiBhv __instance, int buttonIndex)
        {
            try { EstateOptionsTab.Shown(__instance, buttonIndex); }
            catch (Exception e) { Plugin.Log.LogError("options: " + e); }
        }
    }

    [HarmonyPatch(typeof(OptionsMenuUiBhv), nameof(OptionsMenuUiBhv.OnScreenPopped))]
    internal static class EstateOptionsTabClosed
    {
        private static void Postfix(OptionsMenuUiBhv __instance)
        {
            try { EstateOptionsTab.Closed(__instance); }
            catch (Exception e) { Plugin.Log.LogError("options: " + e); }
        }
    }
}
