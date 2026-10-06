using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Assets.Code.Game;
using Assets.Code.Inputs;
using Assets.Code.UI.Managers;
using Assets.Code.UI.Options;
using Assets.Code.UI.Screens;
using Assets.Code.Utils;
using DD2Estate.Dd2;
using DD2Estate.Dev;
using DD2Estate.Estate;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DD2Estate.Menu
{
    /// <summary>
    /// The Estate tab of the options screen from outside the game (dev bridge), so that it can be checked
    /// without pixels:
    ///
    ///   options.state                              the screen (its tabs as the list shows them), the Estate tab, every
    ///                                              row with its value in the entry, in the config FILE and on its control,
    ///                                              where it stands on screen and where a controller goes from it; the folder
    ///   options.set {"row":"fonts","value":"dd1"}  a change as a click makes it: through the row's control when the tab is
    ///                                              up, through the setting when it is not. Rows: fonts, loading (dd2 | dd1),
    ///                                              trinkets, supplies, sound, tutorials (true | false), level (0..1),
    ///                                              folder (a folder, or "steam" for "Find through Steam")
    ///   options.open {"tab":"estate"}              the game's options screen as a player opens it (the pause menu, then
    ///                                              Options; at the main menu the screen alone) on a tab: estate, audio,
    ///                                              graphics, controls, game. {"pause":false}: without the pause menu
    ///   options.close                              the options screen, and the pause menu with it ({"pause":false}: not that)
    ///   options.tab {"tab":"game"}                 another tab of the screen that is up, by its button
    ///   options.folder {"ask":true}                "Change": the game's dialog for a text comes up with the folder
    ///                  {"type":"D:/x"}             the dialog's field holds that, as typed
    ///                  {"confirm":true}            its "Apply" ({"cancel":true}: its "Cancel"); alone: where the dialog stands
    ///   options.tip {"row":"sound"}                a row's tooltip put up without a pointer ({"row":""}: all put away)
    ///   options.scroll {"to":0}                    the tab's page scrolled: 1 its head, 0 its foot; alone: where it stands
    ///   options.nav {"select":"tab"}               the controller's place without a controller: selects the tab's button
    ///               {"select":"sound"}             ... a row's control
    ///               {"move":"down"}                one step as the stick makes it (up, down, left, right), by Unity's own
    ///                                              navigation; {"press":"submit"} | "cancel": the button under it
    /// </summary>
    [EstateModule]
    internal static class EstateOptionsDev
    {
        private static void Register()
        {
            AgentBridge.Register("options.state", o => State());
            AgentBridge.Register("options.set", o =>
            {
                var setting = EstateOptions.Find((string)o["row"]);
                if (setting == null) return "no such row; rows: " + string.Join(", ", Array.ConvertAll(EstateOptions.All, s => s.Id));
                var word = Word(o["value"]);
                string problem;
                var via = "setting";
                var row = EstateOptionsTab.Live?.Rows.Find(r => r.Setting == setting);
                if (row != null)
                {
                    via = "control";
                    problem = ThroughControl(row, word);
                }
                else
                {
                    problem = EstateOptions.SetByWord(setting, word);
                }
                return new { taken = problem == null, problem, via, row = EstateOptions.Describe(setting), folder = setting.Kind == EstateOptions.Kind.Folder ? EstateOptions.Folder() : null };
            });
            AgentBridge.Register("options.open", o =>
            {
                if (!SingletonMonoBehaviour<CommonUiBhv>.HasInstance()) return "no common UI yet";
                Plugin.Host.StartCoroutine(Open((string)o["tab"] ?? "estate", (bool?)o["pause"] ?? true));
                return "opening";
            });
            AgentBridge.Register("options.close", o =>
            {
                if (!SingletonMonoBehaviour<CommonUiBhv>.HasInstance()) return "no common UI yet";
                var common = SingletonMonoBehaviour<CommonUiBhv>.Instance;
                var was = common.IsOptionsMenuActive();
                common.HideOptionsMenu();
                if (((bool?)o["pause"] ?? true) && common.IsPauseMenuActive()) common.HidePauseMenu();
                return was ? "closing" : "the options screen was not up";
            });
            AgentBridge.Register("options.tab", o => Click((string)o["tab"] ?? "estate") ?? State());
            AgentBridge.Register("options.folder", Folder);
            AgentBridge.Register("options.tip", o =>
            {
                var tab = EstateOptionsTab.Live;
                if (tab == null) return "the Estate tab is not up";
                var shown = new List<string>();
                foreach (var row in tab.Rows)
                {
                    if (row.Tip == null) continue;
                    var on = string.Equals(row.Setting.Id, (string)o["row"], StringComparison.OrdinalIgnoreCase);
                    row.Tip.SetActive(on);
                    if (on) shown.Add(row.Setting.Id + ": " + row.Tip.GetComponentInChildren<TMP_Text>(true)?.text + " at " + OnScreen(row.Tip.transform as RectTransform));
                }
                return shown;
            });
            AgentBridge.Register("options.nav", Nav);
            AgentBridge.Register("options.scroll", o =>
            {
                var scroll = EstateOptionsTab.Live?.Scroll;
                if (scroll == null) return "the Estate tab is not up";
                if (o["to"] != null) scroll.verticalNormalizedPosition = Mathf.Clamp01((float)o["to"]);
                return new { position = scroll.verticalNormalizedPosition, content = scroll.content != null ? scroll.content.rect.height : 0f, view = scroll.viewport != null ? scroll.viewport.rect.height : 0f };
            });
        }

        private static string Word(JToken value)
        {
            if (value == null || value.Type == JTokenType.Null) return "";
            if (value.Type == JTokenType.Boolean) return (bool)value ? "true" : "false";
            if (value.Type == JTokenType.Float || value.Type == JTokenType.Integer) return ((double)value).ToString("R", CultureInfo.InvariantCulture);
            return (string)value;
        }

        // A change made the way a pointer makes it: the control is given the value and tells whoever listens.
        private static string ThroughControl(EstateOptionsTab.Row row, string word)
        {
            var setting = row.Setting;
            switch (setting.Kind)
            {
                case EstateOptions.Kind.Choice:
                    var at = Array.FindIndex(setting.Values, v => string.Equals(v, word, StringComparison.OrdinalIgnoreCase));
                    if (at < 0) at = Array.FindIndex(setting.Words, v => string.Equals(v, word, StringComparison.OrdinalIgnoreCase));
                    if (at < 0) return "one of: " + string.Join(", ", setting.Values);
                    row.Dropdown.value = at;
                    return null;
                case EstateOptions.Kind.Switch:
                    if (!bool.TryParse(word, out var on)) return "true or false";
                    row.Toggle.isOn = on;
                    return null;
                case EstateOptions.Kind.Level:
                    if (!float.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out var level)) return "a number from 0 to 1";
                    row.Slider.value = level;
                    return null;
                default:
                    // the folder: its two buttons' errands, the dialog's "Apply" with these words in its field
                    if (string.Equals(word, "steam", StringComparison.OrdinalIgnoreCase)) row.Steam.onClick.Invoke();
                    else EstateOptionsTab.FolderTyped(word);
                    return EstateOptions.FolderProblem;
            }
        }

        // ---- the screen ------------------------------------------------------------------------------------

        private static OptionsMenuUiBhv Screen => UnityEngine.Object.FindObjectOfType<OptionsMenuUiBhv>();

        private static readonly Dictionary<string, OptionsValue.OptionGroup> Groups = new Dictionary<string, OptionsValue.OptionGroup>(StringComparer.OrdinalIgnoreCase)
        {
            { "audio", OptionsValue.OptionGroup.AUDIO }, { "graphics", OptionsValue.OptionGroup.GRAPHICS }, { "controls", OptionsValue.OptionGroup.CONTROL },
            { "game", OptionsValue.OptionGroup.GAME }, { "debug", OptionsValue.OptionGroup.DEBUG }, { "estate", EstateOptionsTab.Group }
        };

        // A tab's button pressed. Null when done, else why not.
        private static string Click(string name)
        {
            var screen = Screen;
            if (screen == null) return "the options screen is not up";
            if (!Groups.TryGetValue(name ?? "", out var group)) return "tab=" + string.Join("|", Groups.Keys);
            var tab = EstateOptionsTab.TabsOf(screen)?.Find(t => t != null && t.m_group == group);
            if (tab?.m_button == null) return "the screen has no such tab";
            tab.m_button.onClick.Invoke();
            return null;
        }

        private static IEnumerator Open(string tab, bool pause)
        {
            var common = SingletonMonoBehaviour<CommonUiBhv>.Instance;
            var menu = GameModeMgr.CurrentMode == GameModeType.MAIN_MENU;
            if (!common.IsOptionsMenuActive())
            {
                if (pause && !menu && !common.IsPauseMenuActive())
                {
                    common.TogglePauseMenu();
                    for (var i = 0; i < 240 && !common.IsPauseMenuActive(); i++) yield return null;
                    yield return new WaitForSecondsRealtime(0.4f);
                }
                // the pause menu's own Options does this (PauseMenuUiControllerBhv.ButtonOptions); the main menu opens the screen as its primary
                common.ShowOptionsMenu(menu);
            }
            OptionsMenuUiBhv screen = null;
            for (var i = 0; i < 600; i++)
            {
                screen = Screen;
                if (screen != null && screen.ScreenState == UiScreenState.Open) break;
                yield return null;
            }
            if (screen == null)
            {
                Plugin.Log.LogWarning("options.open: the options screen did not come up");
                yield break;
            }
            yield return null;
            var problem = Click(tab);
            if (problem != null) Plugin.Log.LogWarning("options.open: " + problem);
        }

        internal static string OnScreen(RectTransform rect)
        {
            if (rect == null || !rect.gameObject.activeInHierarchy) return null;
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            var canvas = rect.GetComponentInParent<Canvas>();
            var camera = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
            var a = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);     // bottom left
            var b = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);     // top right
            return Mathf.RoundToInt(a.x) + "," + Mathf.RoundToInt(a.y) + " .. " + Mathf.RoundToInt(b.x) + "," + Mathf.RoundToInt(b.y);
        }

        // The colour of the diamond before an entry of the list, and where it stands.
        private static string DiamondOf(Selectable entry)
        {
            if (entry == null) return null;
            foreach (var image in entry.GetComponentsInChildren<Image>(true))
                if (image.sprite != null && image.sprite.name == "ui_diamond_white")
                    return "#" + ColorUtility.ToHtmlStringRGBA(image.color) + " at " + OnScreen(image.rectTransform);
            return null;
        }

        private static string NameOf(Selectable selectable) => selectable == null ? null : selectable.name + " < " + (selectable.transform.parent != null ? selectable.transform.parent.name : "");

        private static object Links(Selectable selectable)
        {
            if (selectable == null) return null;
            var nav = selectable.navigation;
            return new { control = NameOf(selectable), mode = nav.mode.ToString(), up = NameOf(nav.selectOnUp), down = NameOf(nav.selectOnDown), left = NameOf(nav.selectOnLeft), right = NameOf(nav.selectOnRight) };
        }

        private static object State()
        {
            var screen = Screen;
            var live = EstateOptionsTab.Live;
            var list = new List<object>();
            object page = null;
            if (screen != null)
            {
                var tabs = EstateOptionsTab.TabsOf(screen);
                if (tabs != null)
                    foreach (var tab in tabs)
                    {
                        if (tab == null) continue;
                        list.Add(new
                        {
                            group = tab.m_group == EstateOptionsTab.Group ? "ESTATE" : tab.m_group.ToString(),
                            button = tab.m_button != null ? tab.m_button.name : null,
                            label = tab.m_button != null ? tab.m_button.GetComponentInChildren<TMP_Text>(true)?.text : null,
                            listed = tab.m_button != null && tab.m_button.gameObject.activeInHierarchy,
                            at = tab.m_button != null ? OnScreen(tab.m_button.transform as RectTransform) : null,
                            shown = tab.m_page != null && tab.m_page.activeInHierarchy,
                            // the diamond before the name, and how far the glow behind the entry is up (0 idle, 0.5 pointed at, 1 chosen)
                            diamond = DiamondOf(tab.m_button),
                            glow = tab.m_highlightCanvasGroup != null ? tab.m_highlightCanvasGroup.alpha : -1f,
                            nav = tab.m_button != null ? Links(tab.m_button) : null
                        });
                    }
            }
            var rows = new List<object>();
            foreach (var setting in EstateOptions.All)
            {
                var row = live?.Rows.Find(r => r.Setting == setting);
                object control = null;
                if (row != null)
                {
                    control = new
                    {
                        shows = row.Dropdown != null ? row.Dropdown.options[row.Dropdown.value].text
                            : row.Toggle != null ? (row.Toggle.isOn ? "on" : "off")
                            : row.Slider != null ? row.Slider.value.ToString("0.###", CultureInfo.InvariantCulture)
                            : row.Folder != null ? EstateOptionsTab.Plain(row.Folder.text) + " | " + EstateOptionsTab.Plain(row.Status.text) : null,
                        name = row.Object.GetComponentInChildren<TMP_Text>(true)?.text,
                        at = OnScreen(row.Object.transform as RectTransform),
                        controlAt = OnScreen(row.First != null ? row.First.transform as RectTransform : null),
                        nav = Links(row.First),
                        second = row.Steam != null ? Links(row.Steam) : null,
                        tip = row.Tip != null ? row.Tip.GetComponentInChildren<TMP_Text>(true)?.text : null,
                        tipUp = row.Tip != null && row.Tip.activeInHierarchy
                    };
                }
                rows.Add(new { setting = EstateOptions.Describe(setting), control });
            }
            if (live != null)
            {
                page = new
                {
                    index = live.Index,
                    shown = live.Entry.m_page.activeInHierarchy,
                    title = Title(live),
                    rows = live.Rows.Count,
                    scroll = live.Scroll != null ? new { at = OnScreen(live.Scroll.transform as RectTransform), content = live.Scroll.content != null ? live.Scroll.content.rect.height : 0f, view = live.Scroll.viewport != null ? live.Scroll.viewport.rect.height : 0f, position = live.Scroll.verticalNormalizedPosition, bar = live.Scroll.verticalScrollbar != null && live.Scroll.verticalScrollbar.gameObject.activeInHierarchy } : null,
                    listSpacing = live.List != null ? live.List.spacing : 0f,
                    listSpacingWas = live.ListSpacingWas,
                    firstSelected = live.Entry.m_firstSelected != null ? live.Entry.m_firstSelected.name : null
                };
            }
            return new
            {
                session = EstateSession.Active,
                mode = GameModeMgr.CurrentMode?.GetName(),
                screen = screen == null ? "closed" : screen.ScreenState.ToString(),
                device = SingletonMonoBehaviour<InputSystemBhv>.HasInstance() ? SingletonMonoBehaviour<InputSystemBhv>.Instance.ActiveInputDevice.ToString() : null,
                selected = EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null ? AgentBridge.PathOf(EventSystem.current.currentSelectedGameObject.transform) : null,
                tabs = list,
                estateTab = page,
                rows,
                folder = EstateOptions.Folder(),
                file = Plugin.Settings.ConfigFilePath,
                savePending = EstateOptions.SavePending,
                window = new[] { UnityEngine.Screen.width, UnityEngine.Screen.height }
            };
        }

        private static string Title(EstateOptionsTab.Tab tab)
        {
            foreach (var text in tab.Entry.m_page.GetComponentsInChildren<TMP_Text>(true))
                if (tab.Entry.m_layout == null || !text.transform.IsChildOf(tab.Entry.m_layout)) return text.text;
            return null;
        }

        // ---- the folder's dialog ---------------------------------------------------------------------------

        private static object Folder(JObject o)
        {
            if ((bool?)o["ask"] == true)
            {
                var row = EstateOptionsTab.Live?.Rows.Find(r => r.Change != null);
                if (row == null) return "the Estate tab is not up (options.open)";
                row.Change.onClick.Invoke();
            }
            var input = EstateOptionsTab.DialogInput(out var dialog);
            if (input == null) return new { dialog = "none", folder = EstateOptions.Folder() };
            if (o["type"] != null)
            {
                input.text = (string)o["type"];
                dialog.OnEditNameComplete();        // what the field calls when the typing ends
            }
            var buttons = HarmonyLib.Traverse.Create(dialog);
            if ((bool?)o["confirm"] == true) buttons.Field("m_AcceptBtn").GetValue<GameObject>().GetComponent<Button>().onClick.Invoke();
            else if ((bool?)o["cancel"] == true) buttons.Field("m_DeclineBtn").GetValue<GameObject>().GetComponent<Button>().onClick.Invoke();
            return new
            {
                dialog = "up",
                title = buttons.Field("m_TitleText").GetValue<TMP_Text>()?.text,
                field = input.text,
                font = input.textComponent != null && input.textComponent.font != null ? input.textComponent.font.name : null,
                size = input.pointSize,
                limit = input.characterLimit,
                at = OnScreen(input.transform as RectTransform),
                textWidth = input.textComponent != null ? input.textComponent.preferredWidth : 0f,
                confirm = buttons.Field("m_AcceptBtn").GetValue<GameObject>()?.GetComponentInChildren<TMP_Text>(true)?.text,
                cancel = buttons.Field("m_DeclineBtn").GetValue<GameObject>()?.GetComponentInChildren<TMP_Text>(true)?.text,
                folder = EstateOptions.Folder()
            };
        }

        // ---- a controller's way without a controller -------------------------------------------------------

        private static object Nav(JObject o)
        {
            var events = EventSystem.current;
            if (events == null) return "no event system";
            var live = EstateOptionsTab.Live;
            var select = (string)o["select"];
            if (select != null)
            {
                GameObject target = null;
                if (select == "tab") target = live?.Entry.m_button.gameObject;
                else if (Groups.TryGetValue(select, out var group)) target = EstateOptionsTab.TabsOf(Screen)?.Find(t => t != null && t.m_group == group)?.m_button.gameObject;
                else target = live?.Rows.Find(r => string.Equals(r.Setting.Id, select, StringComparison.OrdinalIgnoreCase))?.First?.gameObject;
                if (target == null) return "nothing to select by that name";
                events.SetSelectedGameObject(target);
            }
            var selected = events.currentSelectedGameObject;
            var move = (string)o["move"];
            if (move != null)
            {
                if (selected == null) return "nothing is selected";
                var direction = move == "up" ? MoveDirection.Up : move == "down" ? MoveDirection.Down : move == "left" ? MoveDirection.Left : move == "right" ? MoveDirection.Right : MoveDirection.None;
                if (direction == MoveDirection.None) return "move=up|down|left|right";
                var vector = direction == MoveDirection.Up ? Vector2.up : direction == MoveDirection.Down ? Vector2.down : direction == MoveDirection.Left ? Vector2.left : Vector2.right;
                ExecuteEvents.Execute(selected, new AxisEventData(events) { moveDir = direction, moveVector = vector }, ExecuteEvents.moveHandler);
            }
            var press = (string)o["press"];
            if (press != null)
            {
                if (selected == null) return "nothing is selected";
                if (press == "submit") ExecuteEvents.Execute(selected, new BaseEventData(events), ExecuteEvents.submitHandler);
                else if (press == "cancel") ExecuteEvents.Execute(selected, new BaseEventData(events), ExecuteEvents.cancelHandler);
                else return "press=submit|cancel";
            }
            var now = events.currentSelectedGameObject;
            return new
            {
                was = selected != null ? AgentBridge.PathOf(selected.transform) : null,
                selected = now != null ? AgentBridge.PathOf(now.transform) : null,
                links = now != null ? Links(now.GetComponent<Selectable>()) : null,
                pointer = Pointer(),
                scroll = live?.Scroll != null ? live.Scroll.verticalNormalizedPosition : -1f
            };
        }

        // The game's own mark of the row a controller is on.
        private static object Pointer()
        {
            var screen = Screen;
            if (screen == null) return null;
            var pointer = HarmonyLib.Traverse.Create(screen).Field("m_gamepadTooltipPointer").GetValue<RectTransform>();
            if (pointer == null) return null;
            return new { shown = pointer.gameObject.activeInHierarchy, on = pointer.parent != null ? pointer.parent.name : null, at = OnScreen(pointer) };
        }
    }
}
