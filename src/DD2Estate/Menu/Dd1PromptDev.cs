using System;
using System.Collections.Generic;
using System.Globalization;
using Assets.Code.Game;
using DD2Estate.Dd1;
using DD2Estate.Dd2;
using DD2Estate.Dev;
using DD2Estate.Estate;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD2Estate.Menu
{
    /// <summary>
    /// Dev bridge for the Darkest Dungeon (1) folder: where it was found, how to see what a player without it
    /// sees, and the main menu's question without a pointer.
    ///
    ///   dd1.state                        the folder in use, by what it was found, what the search looked at up
    ///                                    to there, what the registry was asked, who asked for the install
    ///                                    while there was none, and whether a folder could be taken into use
    ///                                    now. {"places":true}: every place the search knows, each looked at
    ///   dd1.hide {"on":true}             from now on the mod behaves as if no Darkest Dungeon were found,
    ///                                    whatever the config and Steam say; the config file is not touched.
    ///                                    {"on":false}: found again. What this run has read stays read: for a
    ///                                    player's first start use
    ///   dd1.hide {"next":true}           ... which hides it for the next start of the game, and that one only
    ///                                    ({"next":false} takes it back). Restart the game after it.
    ///   menu.estate                      the main menu's Estate entry, pressed
    ///   dd1.prompt                       the question that is up: its words, where they stand, what is typed
    ///              {"type":"D:/x"}       the field holds that, as typed
    ///              {"confirm":true}      its "Continue" ({"cancel":true}: its "Cancel")
    /// </summary>
    [EstateModule]
    internal static class Dd1PromptDev
    {
        private static void Register()
        {
            // "hidden for the next start" is this start: the mark is taken now, whether or not anything asks
            Dd1Install.HideIfAsked();
            AgentBridge.Register("dd1.state", o => Quietly(() => State((bool?)o["places"] ?? false)));
            AgentBridge.Register("dd1.hide", o => Quietly(() =>
            {
                var done = Dd1Install.Hide((bool?)o["on"], (bool?)o["next"]);
                return new { done, state = State(false) };
            }));
            AgentBridge.Register("menu.estate", o =>
            {
                if (GameModeMgr.CurrentMode != GameModeType.MAIN_MENU) return "not at the main menu";
                EstateMenu.Clicked();
                return new { pressed = true, prompt = Quietly(Prompt), session = EstateSession.Active, starting = EstateSession.Starting };
            });
            AgentBridge.Register("dd1.prompt", o =>
            {
                var dialog = Dd1Prompt.Dialog;
                if (dialog == null) return Quietly(Prompt);
                var fields = Traverse.Create(dialog);
                var input = fields.Field("m_InputField").GetValue<TMP_InputField>();
                if (o["type"] != null && input != null)
                {
                    input.text = (string)o["type"];
                    dialog.OnEditNameComplete();        // what the field calls when the typing ends
                }
                if ((bool?)o["confirm"] == true) fields.Field("m_AcceptBtn").GetValue<GameObject>().GetComponent<Button>().onClick.Invoke();
                else if ((bool?)o["cancel"] == true) fields.Field("m_DeclineBtn").GetValue<GameObject>().GetComponent<Button>().onClick.Invoke();
                return Quietly(Prompt);
            });
        }

        // The bridge's own looks are not written down as asks for the install.
        private static object Quietly(Func<object> ask)
        {
            var noted = Dd1Install.NoteAskers;
            Dd1Install.NoteAskers = false;
            try { return ask(); }
            finally { Dd1Install.NoteAskers = noted; }
        }

        private static object Places(IEnumerable<Dd1Install.Looked> looked)
        {
            var list = new List<object>();
            foreach (var place in looked) list.Add(new { folder = place.Folder, by = place.By, darkestDungeon = place.Is });
            return list;
        }

        private static object State(bool places)
        {
            var root = Dd1Install.Root;
            var asked = new List<object>();
            foreach (var asker in Dd1Install.AskedWhileMissing) asked.Add(new { chain = asker.Key, times = asker.Value });
            var can = Dd1Install.CanUseNow(out var why);
            var next = Dd1Install.Next(out var nextBy);
            return new
            {
                inUse = root,
                foundBy = Dd1Install.FoundBy.ToString(),
                how = Dd1Install.FoundHow,
                hidden = Dd1Install.Hidden,
                hiddenAtNextStart = Dd1Install.HideAtNextStart,
                config = Plugin.Dd1Path.Value,
                configFile = EstateOptions.InFile(EstateOptions.Find(EstateOptions.FolderId)),
                next,
                nextBy = nextBy.ToString(),
                looked = Places(Dd1Install.LastLooked),
                registry = Dd1Install.RegistryAnswers,
                places = places ? Places(Dd1Install.AllPlaces()) : null,
                askedWhileMissing = asked,
                unknownAskers = Dd1Forget.Unknown(Dd1Install.AskerClasses),
                canUseNow = root == null ? (object)can : null,
                whyNot = root == null ? why : null,
                sessionsWithout = Dd1Install.SessionsWithout,
                session = EstateSession.Active,
                mode = GameModeMgr.CurrentMode?.GetName()
            };
        }

        private static object Text(TMP_Text text)
        {
            if (text == null) return null;
            text.ForceMeshUpdate();
            return new
            {
                text = EstateOptionsTab.Plain(text.text),
                at = EstateOptionsDev.OnScreen(text.rectTransform),
                font = text.font != null ? text.font.name : null,
                size = text.fontSize,
                colour = "#" + ColorUtility.ToHtmlStringRGBA(text.color),
                lines = text.textInfo != null ? text.textInfo.lineCount : 0,
                height = text.preferredHeight.ToString("0.#", CultureInfo.InvariantCulture),
                overflowing = text.isTextOverflowing
            };
        }

        private static object Prompt()
        {
            var dialog = Dd1Prompt.Dialog;
            if (dialog == null) return new { dialog = "none", found = Dd1Install.Root != null, status = Dd1Prompt.Status };
            var fields = Traverse.Create(dialog);
            var input = fields.Field("m_InputField").GetValue<TMP_InputField>();
            var accept = fields.Field("m_AcceptBtn").GetValue<GameObject>();
            var decline = fields.Field("m_DeclineBtn").GetValue<GameObject>();
            var title = fields.Field("m_TitleText").GetValue<TMP_Text>();
            return new
            {
                dialog = "up",
                at = EstateOptionsDev.OnScreen(dialog.transform as RectTransform),
                title = Text(title),
                explanation = Text(Dd1Prompt.ExplainText),
                field = input == null ? null : new
                {
                    text = input.text,
                    at = EstateOptionsDev.OnScreen(input.transform as RectTransform),
                    font = input.textComponent != null && input.textComponent.font != null ? input.textComponent.font.name : null,
                    size = input.pointSize,
                    limit = input.characterLimit,
                    focused = input.isFocused,
                    usable = input.interactable
                },
                status = Text(Dd1Prompt.StatusText),
                refusal = Dd1Prompt.StatusBad,
                confirm = accept == null ? null : new { text = accept.GetComponentInChildren<TMP_Text>(true)?.text, shown = accept.activeInHierarchy, at = EstateOptionsDev.OnScreen(accept.transform as RectTransform) },
                cancel = decline == null ? null : new { text = decline.GetComponentInChildren<TMP_Text>(true)?.text, shown = decline.activeInHierarchy, at = EstateOptionsDev.OnScreen(decline.transform as RectTransform) },
                found = Dd1Install.Root != null,
                config = Plugin.Dd1Path.Value,
                window = new[] { Screen.width, Screen.height }
            };
        }
    }
}
