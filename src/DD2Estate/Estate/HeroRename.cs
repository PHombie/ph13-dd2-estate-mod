using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Assets.Code.Actor;
using DD2Estate.Dd1;
using DD2Estate.Dd2;
using DD2Estate.Dev;
using DD2Estate.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD2Estate.Estate
{
    /// <summary>
    /// DD1 lets a hero be renamed on its character sheet: a click on the quill (shared/character/icon_rename.png,
    /// tooltip "Rename Hero") makes the name a text field that starts with the name as it is, and a second click
    /// writes what stands there into the hero (exe 0xb82400, 0xb7da40). The estate's hero sheet is DD2's, so the
    /// name is written where the hamlet shows it large: the hero banner of the Guild, the Blacksmith and the
    /// Survivalist. A click on the name there opens the field; Enter or a click elsewhere takes the name, Escape
    /// gives it up.
    ///
    /// DD1's limits (the exe, same place): as many characters as the text "hero_name_limit" of
    /// localization/names.string_table.xml is long ("1234567890123456": 16), whatever characters the keyboard
    /// gives, and no other check. The estate adds what its own screens need: no line breaks or control
    /// characters, no angle brackets (its labels read them as mark-up), no spaces at the ends, and a name left
    /// empty leaves the hero's name as it was (as DD2's own sheet does). The name is the DD2 actor's, so every
    /// screen of either game follows, and it is saved with the actor.
    /// </summary>
    [EstateModule]
    internal static class HeroRename
    {
        private const string NamesTable = "localization/names.string_table.xml";
        private const string LimitId = "hero_name_limit";
        /// <summary>FALLBACK: the length of DD1's own hero_name_limit, for an install whose table cannot be read.</summary>
        private const int StockLimit = 16;

        private static int _limit;
        private static int _cancelFrame = -10;
        private static readonly List<HeroNameEditor> Editors = new List<HeroNameEditor>();

        /// <summary>True while a name is being written.</summary>
        public static bool Editing { get; internal set; }

        /// <summary>The most characters a hero's name may have: the length of DD1's text hero_name_limit.</summary>
        public static int Limit
        {
            get
            {
                if (_limit > 0) return _limit;
                _limit = StockLimit;
                try
                {
                    var path = Dd1Install.PathOf(NamesTable);
                    if (path != null && File.Exists(path))
                    {
                        var found = new Dictionary<string, string>();
                        Dd1Strings.ReadEnglish(File.ReadLines(path), found, new[] { LimitId });
                        if (found.TryGetValue(LimitId, out var text) && text.Trim().Length > 0) _limit = new System.Globalization.StringInfo(text.Trim()).LengthInTextElements;
                    }
                }
                catch (Exception e) { Plugin.Log.LogWarning("Hero rename: DD1's name limit could not be read, " + StockLimit + " stands: " + e.Message); }
                return _limit;
            }
        }

        private static void Register()
        {
            // {"guid":N,"name":"..."}: the name as the field would take it. {"guid":N} alone: the hero's name and the limit.
            // {"edit":true}: opens the field on the banner of the window that is open (its hero), to be looked at.
            AgentBridge.Register("roster.rename", o =>
            {
                if ((bool?)o["edit"] ?? false)
                {
                    foreach (var editor in Editors)
                        if (editor != null && editor.isActiveAndEnabled && editor.Begin()) return new { editing = true, limit = Limit };
                    return "no hero banner with a hero in it is on screen (open the Guild, the Blacksmith or the Survivalist and pick a hero)";
                }
                if (o["guid"] == null) return "which hero? {\"guid\":N,\"name\":\"...\"}";
                var guid = (uint)o["guid"];
                var actor = UpgradeUi.Hero(guid);
                if (actor == null) return "no living hero #" + guid;
                var was = actor.ActorName;
                if (o["name"] == null) return new { guid, name = was, limit = Limit };
                var refused = Apply(guid, (string)o["name"]);
                return new { guid, was, name = actor.ActorName, limit = Limit, renamed = refused == null, refused };
            });
        }

        /// <summary>A typed name as the estate keeps it; empty when nothing of it is left.</summary>
        public static string Clean(string typed)
        {
            if (string.IsNullOrEmpty(typed)) return "";
            var name = new StringBuilder(typed.Length);
            foreach (var c in typed)
            {
                if (char.IsControl(c) || c == '<' || c == '>') continue;
                name.Append(c);
            }
            var clean = name.ToString().Trim();
            // DD1 counts characters, not bytes: a letter made of two code units is one
            var info = new System.Globalization.StringInfo(clean);
            if (info.LengthInTextElements > Limit) clean = info.SubstringByTextElements(0, Limit).TrimEnd();
            return clean;
        }

        /// <summary>Renames a living hero. Null when done; otherwise why not (the name is as it was).</summary>
        public static string Apply(uint guid, string typed)
        {
            var actor = UpgradeUi.Hero(guid);
            if (actor == null) return "no such hero";
            var name = Clean(typed);
            if (name.Length == 0) return "a hero keeps a name: nothing was written";
            if (name == actor.ActorName) return null;
            var was = actor.ActorName;
            actor.SetActorName(name);
            // the game's own screens that show a name listen for this
            try { Assets.Code.UI.Events.EventHeroNameChanged.Trigger(guid); }
            catch (Exception e) { Plugin.Log.LogWarning("Hero rename: the game's screens were not told: " + e.Message); }
            Plugin.Log.LogInfo("Estate roster: " + was + " (#" + guid + ") is " + name + " from now on");
            return null;
        }

        /// <summary>
        /// Makes a label that shows a hero's name the place to rename the hero: a click on it opens the field.
        /// <paramref name="hero"/> says whose name the label shows (0: nobody's).
        /// </summary>
        public static void Attach(TextMeshProUGUI label, Func<uint> hero, RosterWindow window = null)
        {
            if (label == null || hero == null) return;
            var editor = label.gameObject.GetComponent<HeroNameEditor>() ?? label.gameObject.AddComponent<HeroNameEditor>();
            editor.Label = label;
            editor.Hero = hero;
            editor.Window = window;
            editor.Listen();
            Editors.RemoveAll(e => e == null);
            if (!Editors.Contains(editor)) Editors.Add(editor);
        }

        internal static void Cancelled() => _cancelFrame = Time.frameCount;

        /// <summary>Escape gives up a name being written; the game's menu, which is on the same key, stays shut for it.</summary>
        internal static bool TakesEscape() => Editing || Time.frameCount - _cancelFrame <= 1;
    }

    /// <summary>The text field over a hero's name (<see cref="HeroRename.Attach"/>).</summary>
    internal class HeroNameEditor : MonoBehaviour
    {
        public TextMeshProUGUI Label;
        public Func<uint> Hero;
        public RosterWindow Window;

        private TMP_InputField _field;
        private RectTransform _hit;
        private string _hitFor;
        private uint _guid;
        private bool _editing, _put, _caretToEnd;

        /// <summary>
        /// The place that takes the click: as wide as the name is written, not as wide as the label's box, so the
        /// rest of the banner stays the window's own (a right click there still leaves the building).
        /// </summary>
        public void Listen()
        {
            if (_hit != null || Label == null) return;
            var labelRect = (RectTransform)Label.transform;
            var image = UiKit.Image("HeroNameHit", labelRect.parent, null, new Color(0f, 0f, 0f, 0.004f), true);
            _hit = (RectTransform)image.transform;
            _hit.anchorMin = labelRect.anchorMin;
            _hit.anchorMax = labelRect.anchorMax;
            _hit.pivot = labelRect.pivot;
            _hit.anchoredPosition = labelRect.anchoredPosition;
            _hit.sizeDelta = new Vector2(0f, labelRect.sizeDelta.y);
            _hit.SetSiblingIndex(labelRect.GetSiblingIndex() + 1);
            UpgradeUi.Pointer(image, () => Begin(), null, () => Hover(true), () => Hover(false));
            _hit.gameObject.SetActive(isActiveAndEnabled);
        }

        private void OnEnable()
        {
            if (_hit != null) _hit.gameObject.SetActive(true);
            _hitFor = null;
        }

        /// <summary>Opens the field with the hero's name in it, as DD1 does; false when the label shows nobody.</summary>
        public bool Begin()
        {
            if (_editing) return true;
            var guid = Hero != null ? Hero() : 0u;
            var actor = UpgradeUi.Hero(guid);
            if (actor == null || Label == null) return false;
            try
            {
                if (_field == null) Build();
                _guid = guid;
                Hover(false);
                _field.characterLimit = HeroRename.Limit;
                _field.SetTextWithoutNotify(actor.ActorName ?? "");
                // the label stays where it is (its owner switches it on and off) and is not drawn under the field
                Label.alpha = 0f;
                _field.gameObject.SetActive(true);
                _field.ActivateInputField();
                _caretToEnd = true;
                _editing = true;
                HeroRename.Editing = true;
                EstateAudio.Ui("ui/town/button_click");
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Hero rename: the name's field could not be opened: " + e);
                Put();
                return false;
            }
        }

        public void Hover(bool on)
        {
            if (Window == null || Label == null) return;
            var rect = (RectTransform)Label.transform;
            // DD1's tooltip of its quill: "Rename Hero"
            var at = new Vector2(rect.anchoredPosition.x, -rect.anchoredPosition.y + Label.fontSize + 14f);
            Window.Tip(this, on && !_editing && Hero != null && Hero() != 0u, at, null, WindowText.Plain("character_panel_rename_tt") ?? "Rename Hero");
        }

        private void Build()
        {
            var labelRect = (RectTransform)Label.transform;
            var go = new GameObject("HeroNameField", typeof(RectTransform));
            go.SetActive(false);
            var rect = (RectTransform)go.transform;
            rect.SetParent(labelRect.parent, false);
            rect.anchorMin = labelRect.anchorMin;
            rect.anchorMax = labelRect.anchorMax;
            rect.pivot = labelRect.pivot;
            rect.anchoredPosition = labelRect.anchoredPosition;
            rect.sizeDelta = labelRect.sizeDelta;
            rect.SetSiblingIndex((_hit != null ? _hit.GetSiblingIndex() : labelRect.GetSiblingIndex()) + 1);

            // GUESS: how DD1 marks the name while it is written was not seen; here a faint dark ground under it.
            var ground = go.AddComponent<Image>();
            ground.color = new Color(0f, 0f, 0f, 0.3f);
            ground.raycastTarget = true;

            var area = UiKit.Rect("Area", rect).Stretch();
            area.gameObject.AddComponent<RectMask2D>();
            var text = UiKit.Text("Text", area, "", Dd1Text.Header, Label.color, Label.alignment);
            text.font = Label.font;
            text.fontSize = Label.fontSize;
            text.extraPadding = Label.extraPadding;
            text.enableAutoSizing = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.richText = false;
            ((RectTransform)text.transform).Stretch();

            _field = go.AddComponent<TMP_InputField>();
            _field.transition = Selectable.Transition.None;
            _field.targetGraphic = ground;
            _field.textViewport = area;
            _field.textComponent = text;
            _field.fontAsset = Label.font;
            _field.pointSize = Label.fontSize;
            _field.lineType = TMP_InputField.LineType.SingleLine;
            _field.richText = false;
            _field.onFocusSelectAll = false;
            _field.restoreOriginalTextOnEscape = true;
            _field.customCaretColor = true;
            _field.caretColor = Label.color;
            _field.caretWidth = 2;
            _field.selectionColor = new Color(0.78f, 0.7f, 0.43f, 0.35f);
            _field.onEndEdit.AddListener(End);
        }

        // Enter, or a click elsewhere, takes the name; Escape gives it up (the field has put the old text back by then).
        private void End(string typed)
        {
            if (!_editing) return;
            _editing = false;
            HeroRename.Editing = false;
            if (_field.wasCanceled) HeroRename.Cancelled();
            else
            {
                var refused = HeroRename.Apply(_guid, typed);
                if (refused != null) Window?.Say(refused);
                var actor = UpgradeUi.Hero(_guid);
                if (actor != null && Hero != null && Hero() == _guid) Label.text = actor.ActorName;
            }
            // the field is put away a frame later: it is in the middle of its own closing here
            _put = true;
        }

        private void Put()
        {
            _put = false;
            _editing = false;
            HeroRename.Editing = false;
            if (Label != null) Label.alpha = 1f;
            if (_field != null && _field.gameObject.activeSelf) _field.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (_put) Put();
            if (_hit != null && Label != null && !ReferenceEquals(_hitFor, Label.text))
            {
                _hitFor = Label.text;
                var box = ((RectTransform)Label.transform).sizeDelta;
                var written = string.IsNullOrEmpty(_hitFor) ? 0f : Mathf.Min(box.x, Label.GetPreferredValues(_hitFor).x + 12f);
                _hit.sizeDelta = new Vector2(written, box.y);
            }
            if (!_editing || _field == null) return;
            if (_caretToEnd && _field.isFocused)
            {
                _caretToEnd = false;
                _field.MoveTextEnd(false);
            }
            // another hero took the slot while the name was open: the field closes, and what was written goes to
            // the hero it was written for
            if (Hero == null || Hero() != _guid) _field.DeactivateInputField();
        }

        // The banner hides the name when its slot is empty, and goes with its window.
        private void OnDisable()
        {
            if (_editing || _put || (_field != null && _field.gameObject.activeSelf)) Put();
            if (_hit != null) _hit.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_editing) HeroRename.Editing = false;
            if (_field != null) Destroy(_field.gameObject);
            if (_hit != null) Destroy(_hit.gameObject);
        }
    }
}
