using System;
using System.Collections;
using Assets.Code.Audio;
using Assets.Code.Data;
using Assets.Code.UI.Managers;
using Assets.Code.UI.Widgets;
using Assets.Code.Utils;
using DD2Estate.Dd1;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace DD2Estate.Menu
{
    /// <summary>
    /// The main menu's Estate entry pressed with no Darkest Dungeon (1) to be found: the Estate is not entered;
    /// a pop-up says what is missing and why, and takes the folder. (The owner, 2026-10-06, in translation: "if
    /// the user presses the Estate button and DD1's files are not found, they must be offered to enter the path
    /// to the folder in a pop-up window".)
    ///
    /// The pop-up is the game's own dialog for a text (NameInputDialogBhv, the one the options' folder row
    /// uses), in the instance the game makes for this once, with two things added to it: a few lines under its
    /// title that say what is asked for, and a line under its field for what is wrong with a folder. Both are
    /// copies of the text of the game's confirmation dialog. The game's dialog closes whatever its "confirm"
    /// is given; this one stays open over a wrong folder (a prefix on the dialog's OnConfirmPressed holds it).
    ///
    /// A folder is looked at with the rule of the options' row (Dd1Install.Check, Core/Dd1Folder.cs: the same
    /// forgiveness, the same sentences). A right one is written to the config and, since nothing has been read
    /// from Darkest Dungeon in a run in which none was found (Dd1/Dd1Forget.cs says why that holds and how it
    /// is checked), taken into use on the spot: the pop-up closes and the press on Estate goes on where it
    /// stopped. Where it cannot be taken at once the pop-up says "restart the game to play the Estate" and
    /// stays, with a way out. Cancel, the dialog's hotkey: back to the menu, nothing changed.
    ///
    /// Before it asks, the entry looks once more: an install that has appeared since the game started (or a
    /// folder since written into the config file) is found and used without a question.
    ///
    /// Dev bridge: see <see cref="Dd1PromptDev"/>.
    /// </summary>
    internal static class Dd1Prompt
    {
        public const string Title = "Darkest Dungeon Not Found";
        public const string Explanation = "The Estate reads its Hamlet, dungeons, sound and words from your own copy of Darkest Dungeon, and none was found.\nType or paste the folder it is installed in.";
        public const string RestartLine = "Restart the game to play the Estate.";
        public const string ByPrompt = "named at the main menu";
        private const int FolderLimit = 260;
        /// <summary>
        /// A refusal is red, the Estate's colour of harm (#B11900), written out here: the mod's own red is read
        /// from Darkest Dungeon's colour table, and this is the one place where there is none to read.
        /// </summary>
        public static readonly Color Refusal = new Color32(177, 25, 0, 255);

        // The dialog is 910 by 515 of the game's canvas (1920 by 1080): its title at the head, its field in the
        // middle, its two buttons at the foot. Measures in those units, from the dialog's head down.
        /// <summary>How much taller than the game's the dialog is made, for the lines to have air.</summary>
        public static float Extra = 40f;
        public static float TitleSize = 60f;
        /// <summary>The explanation: its head under the title, its width (the frame's skulls stand in the dialog's upper corners), its letters.</summary>
        public static float ExplainTop = 104f, ExplainWidth = 640f, ExplainSize = 26f;
        /// <summary>The line under the field: its gap to the field's rule, its width, its letters.</summary>
        public static float StatusGap = 10f, StatusWidth = 760f, StatusSize = 24f;
        public static float InputSize = 30f;
        private const float DialogHeight = 515f;       // the dialog's own (its LayoutElement's minimum)
        private const float RuleUnderMiddle = 27.5f;   // the field's rule lies this far under the dialog's middle

        private static NameInputDialogBhv _dialog;
        private static TMP_Text _explain, _status;
        private static Action _then;
        private static bool _taken, _done;
        private static string _carried, _carriedProblem;

        /// <summary>What the line under the field says; null when it says nothing. <see cref="StatusBad"/>: it is a refusal (red).</summary>
        public static string Status { get; private set; }
        public static bool StatusBad { get; private set; }

        /// <summary>The dialog of the question while it is up; null otherwise.</summary>
        public static NameInputDialogBhv Dialog => _dialog != null ? _dialog : null;

        internal static TMP_Text ExplainText => _explain;
        internal static TMP_Text StatusText => _status;

        /// <summary>
        /// True: Darkest Dungeon is there (found before, or just now) and the caller goes on. False: it is not;
        /// the question is up, and <paramref name="then"/> is called once a folder has been taken into use.
        /// </summary>
        public static bool Have(Action then)
        {
            if (Dd1Install.Found) return true;
            // once more, as things stand now
            var next = Dd1Install.Next(out var by, out var how);
            if (next != null && Dd1Install.Use(next, by, how + ", on a second look", out _)) return true;
            Ask(then, next);
            return false;
        }

        private static void Ask(Action then, string waiting)
        {
            if (!SingletonMonoBehaviour<CommonUiBhv>.HasInstance()) return;
            _then = then;
            _taken = false;
            _done = waiting != null;
            var start = _carried ?? DD2Estate.Core.Dd1Folder.Clean(Plugin.Dd1Path.Value);
            if (waiting != null) start = waiting;
            var problem = _carriedProblem;
            _carried = _carriedProblem = null;
            SingletonMonoBehaviour<CommonUiBhv>.Instance.ShowNameInputDialog(Title, start, FolderLimit, Typed, "Continue", typed => Cancelled(), "Cancel");
            try { Dress(start); }
            catch (Exception e) { Plugin.Log.LogWarning("main menu: the question for the Darkest Dungeon folder keeps the look of the game's plain dialog: " + e.Message); }
            if (waiting != null)
            {
                // a right folder is known already (the config's, or Steam's) and cannot be taken in this run
                Dd1Install.CanUseNow(out var why);
                Plugin.Log.LogInfo("main menu: Darkest Dungeon is at " + waiting + " but is not taken into use in this run: " + why);
                Say("Darkest Dungeon is at this folder. " + RestartLine, false);
                OnlyTheWayOut();
            }
            else if (problem != null) Say(problem, true);
            else Say(null, false);
            Plugin.Log.LogInfo("main menu: no Darkest Dungeon 1; the Estate entry asks for its folder");
        }

        // ---- the game's dialog, dressed ----------------------------------------------------------------------

        private static void Dress(string start)
        {
            var input = EstateOptionsTab.DialogInput(out var dialog);
            _dialog = dialog;
            _explain = _status = null;
            if (dialog == null) return;
            var widget = (RectTransform)dialog.transform;

            // a text of the game's to copy: what its confirmation dialog says under its title
            var body = BodyText();
            if (input != null)
            {
                // a path is set in the game's text face, smaller than a name: the heading face has no backslash
                input.richText = false;
                if (body != null && body.font != null) input.fontAsset = body.font;
                input.pointSize = InputSize;
                if (input.textComponent != null)
                {
                    input.textComponent.richText = false;
                    // a path's "\n" and "\t" are a backslash and a letter, not a new line and a tab
                    input.textComponent.parseCtrlCharacters = false;
                    input.textComponent.fontSize = InputSize;
                }
                input.text = start ?? "";
            }
            dialog.SetTitleFontSize(Mathf.RoundToInt(TitleSize));

            // taller by Extra: the field stays in the middle, the buttons go down with the foot
            var element = widget.GetComponent<LayoutElement>();
            if (element != null) element.minHeight = DialogHeight + Extra;
            var accept = Traverse.Create(dialog).Field("m_AcceptBtn").GetValue<GameObject>();
            var buttons = accept != null ? accept.transform.parent as RectTransform : null;
            if (buttons != null && buttons != widget) buttons.anchoredPosition += new Vector2(0f, -Extra);

            if (body != null)
            {
                _explain = Line(body, widget, "DD2Estate.Explanation", ExplainTop, ExplainWidth, ExplainSize, body.color);
                _explain.text = Explanation;
                _status = Line(body, widget, "DD2Estate.Status", (DialogHeight + Extra) * 0.5f + RuleUnderMiddle + StatusGap, StatusWidth, StatusSize, body.color);
            }
            var root = widget.parent as RectTransform;
            if (root != null) LayoutRebuilder.ForceRebuildLayoutImmediate(root);
        }

        // The text the game's confirmation dialog shows under its title (the prefab's own: nothing is changed there).
        private static TMP_Text BodyText()
        {
            var common = SingletonMonoBehaviour<CommonUiBhv>.Instance;
            foreach (var field in new[] { "m_hotkeyCloseableConfirmationPrefab", "m_mainMenuExitGameConfirmationPrefab" })
            {
                var prefab = Traverse.Create(common).Field(field).GetValue<GameObject>();
                if (prefab == null) continue;
                foreach (var behaviour in prefab.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (behaviour == null || behaviour.GetType().Name != "UiDisplayTextBhv") continue;
                    if (Traverse.Create(behaviour).Field("m_Key").GetValue<string>() != "confirmation_desc") continue;
                    var text = behaviour.GetComponent<TMP_Text>();
                    if (text != null) return text;
                }
            }
            Plugin.Log.LogWarning("main menu: the game's confirmation dialog has no text to copy; the folder's question goes without its explanation");
            return null;
        }

        // A copy of that text as a line of the dialog: its head at a height, in the middle, as wide as told.
        private static TMP_Text Line(TMP_Text source, RectTransform widget, string name, float top, float width, float size, Color colour)
        {
            var old = widget.Find(name);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            // made inside a holder that is off, so that what the game hangs on its text never wakes
            var holder = new GameObject("DD2Estate.Parts");
            holder.SetActive(false);
            holder.transform.SetParent(widget, false);
            try
            {
                var go = Object.Instantiate(source.gameObject, holder.transform, false);
                go.name = name;
                EstateOptionsTab.Strip(go);
                foreach (var fitter in go.GetComponents<ContentSizeFitter>()) Object.DestroyImmediate(fitter);
                foreach (var element in go.GetComponents<LayoutElement>()) Object.DestroyImmediate(element);
                for (var i = go.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(go.transform.GetChild(i).gameObject);
                go.AddComponent<LayoutElement>().ignoreLayout = true;
                var line = go.GetComponent<TMP_Text>();
                line.text = "";
                line.richText = false;
                line.parseCtrlCharacters = false;
                line.enableAutoSizing = false;
                line.fontSize = size;
                line.color = colour;
                line.alignment = TextAlignmentOptions.Top;
                line.textWrappingMode = TextWrappingModes.Normal;
                line.overflowMode = TextOverflowModes.Overflow;
                line.margin = Vector4.zero;
                line.raycastTarget = false;
                var rect = (RectTransform)go.transform;
                rect.SetParent(widget, false);
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition = new Vector2(0f, -top);
                rect.sizeDelta = new Vector2(width, size * 4f);
                rect.localScale = Vector3.one;
                go.SetActive(true);
                return line;
            }
            finally { Object.Destroy(holder); }
        }

        private static void Say(string words, bool bad)
        {
            Status = string.IsNullOrEmpty(words) ? null : words;
            StatusBad = bad && Status != null;
            if (_status == null) return;
            _status.text = EstateOptionsTab.Breakable(Status ?? "");
            _status.color = StatusBad ? Refusal : (_explain != null ? _explain.color : Color.grey);
        }

        // The folder is saved and waits for a restart: nothing is left to confirm, the other button closes.
        private static void OnlyTheWayOut()
        {
            _done = true;
            if (_dialog == null) return;
            var fields = Traverse.Create(_dialog);
            var accept = fields.Field("m_AcceptBtn").GetValue<GameObject>();
            if (accept != null) accept.SetActive(false);
            var context = _dialog.GetComponent<DataContextBhv>();
            if (context != null) context.SetStringValue("decline_label", "Close");
            var input = fields.Field("m_InputField").GetValue<TMP_InputField>();
            if (input != null)
            {
                input.DeactivateInputField();
                input.interactable = false;
            }
        }

        // ---- what is typed ---------------------------------------------------------------------------------

        /// <summary>
        /// The dialog's "confirm" is pressed (its button, or the key). False holds the dialog open: the folder is
        /// wrong, or it is saved for the next start. True lets the game's dialog go on: it calls
        /// <see cref="Typed"/> and closes.
        /// </summary>
        internal static bool Confirming(NameInputDialogBhv dialog)
        {
            if (_dialog == null || dialog != _dialog) return true;
            try
            {
                if (_done) return false;
                var input = Traverse.Create(dialog).Field("m_InputField").GetValue<TMP_InputField>();
                var typed = input != null ? EstateOptionsTab.Plain(input.text) : null;
                if (Take(typed))
                {
                    _taken = true;
                    Sound(true);
                    return true;
                }
                Sound(!StatusBad);
                if (_done) OnlyTheWayOut();
                else if (input != null && Plugin.Host != null) Plugin.Host.StartCoroutine(BackToTheField(dialog, input));
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("main menu: the Darkest Dungeon folder could not be looked at: " + e);
                Say("That folder could not be looked at: " + e.Message, true);
                return false;
            }
        }

        // The folder as typed. True: it is in use. False: Status says why not (wrong: red; saved for a restart: not).
        private static bool Take(string typed)
        {
            var folder = Dd1Install.Check(typed, out var problem);
            if (folder == null)
            {
                Say(problem ?? "That folder cannot be used.", true);
                Plugin.Log.LogInfo("main menu: the folder '" + typed + "' was not taken: " + Status);
                return false;
            }
            Plugin.Dd1Path.Value = folder;
            Dd1Install.Named(folder);
            if (Dd1Install.Use(folder, Dd1Install.Origin.ByHand, ByPrompt, out var why))
            {
                Say(null, false);
                Plugin.Log.LogInfo("main menu: Darkest Dungeon folder named and in use: " + folder);
                return true;
            }
            _done = true;
            Say("Saved. " + RestartLine, false);
            Plugin.Log.LogInfo("main menu: Darkest Dungeon folder named and saved: " + folder + "; in use after a restart (" + why + ")");
            return false;
        }

        // The key that confirms also ends the typing: the field is given back to the keyboard.
        private static IEnumerator BackToTheField(NameInputDialogBhv dialog, TMP_InputField input)
        {
            yield return null;
            if (dialog == null || input == null || _dialog != dialog || !input.gameObject.activeInHierarchy) yield break;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(input.gameObject);
            input.ActivateInputField();
        }

        // The game's dialog has closed on "confirm" and hands over what it holds.
        private static void Typed(string typed)
        {
            var then = _then;
            _then = null;
            _dialog = null;
            if (_taken)
            {
                _taken = false;
                if (then != null) EstateMenu.Later(then);
                return;
            }
            // (the dialog was not held: its "confirm" went past the prefix. The folder is looked at here, and
            // the question comes back with what is wrong.)
            if (_done) return;
            if (Take(typed))
            {
                if (then != null) EstateMenu.Later(then);
                return;
            }
            _carried = typed;
            _carriedProblem = _done ? null : Status;
            if (!_done) EstateMenu.Later(() => Ask(then, null));
        }

        private static void Cancelled()
        {
            _then = null;
            _dialog = null;
            _taken = false;
            Status = null;
            StatusBad = false;
        }

        private static void Sound(bool good)
        {
            try { SingletonMonoBehaviour<AudioMgr>.Instance.Play(good ? AudioPathsBhv.ClickConfirm : AudioPathsBhv.ClickInvalid); }
            catch (Exception) { }
        }
    }

    /// <summary>The folder's question holds the game's dialog open over a wrong folder (the dialog closes on any "confirm").</summary>
    [HarmonyPatch(typeof(NameInputDialogBhv), nameof(NameInputDialogBhv.OnConfirmPressed))]
    internal static class Dd1PromptHoldsTheDialog
    {
        private static bool Prefix(NameInputDialogBhv __instance) => Dd1Prompt.Confirming(__instance);
    }
}
