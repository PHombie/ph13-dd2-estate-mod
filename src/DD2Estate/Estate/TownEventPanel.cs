using System;
using System.Collections.Generic;
using Assets.Code.UI.Managers;
using Assets.Code.Utils;
using DD2Estate.Dd1;
using DD2Estate.Dd2;
using DD2Estate.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DD2Estate.Estate
{
    /// <summary>
    /// The town crier's notice, DD1's "Hear Ye, Hear Ye!" screen: the crier in front of his backdrop and, on the
    /// scroll, the event's title, picture, story and what it does this week, inside a frame coloured by the
    /// event's tone. Art and places are DD1's (campaign/town/town_event: town_event.background.png, .character,
    /// .tone_frame_&lt;tone&gt;, .image_&lt;event&gt;, and town_event.layout.darkest, whose numbers are offsets
    /// in the backdrop's pixels, x at the middle of an element and y at its top).
    ///
    /// The notice comes up by itself once when the week begins (after the hamlet's own report has been read),
    /// and stays within reach all week behind DD1's bell on the estate's bar (fx/estate_town_event, the fifth
    /// button of the bar's row: <see cref="HasNotice"/> puts it there and <see cref="Toggle"/> is its click).
    /// tools/preview_town_event.py draws the same layout offline: change one, change the other.
    /// </summary>
    [EstateModule]
    internal class TownEventPanel : MonoBehaviour
    {
        private const string Dir = "campaign/town/town_event/";
        private const string Art = Dir + "town_event.";
        private const string LayoutFile = Dir + "town_event.layout.darkest";
        private const string TownLayout = "campaign/town/town.layout.darkest";
        private const string SummaryLayout = "campaign/town/estate_summary/estate_summary.layout.darkest";
        private const string GraveFrame = "shared/hero/roster_icon_frame_town_event.png";

        /// <summary>The bell's button on the estate's bar (<see cref="TownPanelButtons"/>).</summary>
        public const string BellId = "town_event";
        private const string BellSheet = "fx/estate_town_event/estate_town_event.sprite.png";
        // DD1's screen: the notice sits where a building's backdrop does (town_screen_layout town_event_pos), the
        // estate's bar begins where its layout puts it (122 px above the screen's foot), 1550 px lie left of the
        // roster column.
        private static readonly Vector2 Dd1Pos = new Vector2(144f, 132f);
        private const float ScreenHeight = 1080f;
        private const float RoomWidth = 1920f - RosterPanel.Width;
        private static readonly Vector2 CloseSize = new Vector2(32f, 32f);       // shared/progression/progression_close.png
        private const float DescriptionHeight = 142f;   // from DD1's description line down to the frame's lower band
        private const float InfoHeight = 92f;           // from DD1's first line to the end of the frame's lower band (rows 516-624 of the art)
        private static readonly Vector2 GraveSize = new Vector2(85f, 85f);     // roster_icon_frame_town_event.png: a hero's portrait

        private static TownEventPanel _open;
        private static float _nextComplaint;

        private RectTransform _frame, _graves;
        private TextMeshProUGUI _info;
        private Dd1Tooltip _tooltip;
        private string _eventId;
        private string _message;
        private Vector2 _resultPos, _infoStart, _graveStart, _graveStep, _framePos;
        private float _textWidth, _graveArea, _graveFade, _fadeAt = -1f;
        // The fallen on the scroll, and the one of them that was taken (the others fade).
        private readonly List<KeyValuePair<string, CanvasGroup>> _graveRow = new List<KeyValuePair<string, CanvasGroup>>();
        private string _chosen;

        public static bool IsOpen => _open != null;

        /// <summary>The week has an event whose notice can be brought back: when DD1's bar shows its bell.</summary>
        public static bool HasNotice
        {
            get
            {
                var active = TownEvents.Current;
                return active != null && active.Applied && active.Def != null;
            }
        }

        /// <summary>What a click on DD1's bell does: the notice up, or away again.</summary>
        public static void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        /// <summary>The week's notice has not come up yet and will: the Ancestor's remark on the event waits for it.</summary>
        public static bool Pending
        {
            get
            {
                var active = TownEvents.Current;
                return active != null && active.Applied && !active.Shown && active.Def != null && HamletScreen.IsOpen;
            }
        }

        private static void Register()
        {
            NarrationMoments.Tick += OnTick;
            NarrationMoments.SessionEnded += Close;
            TownEvents.Changed += OnChanged;

            // DD1's bell on the estate's bar, in a week with an event: after the scroll, at 1360 on DD1's own
            // screen. The bar plays the sprite itself (its "idle": the bell before a rolled scroll; "selected"
            // while the notice is up: the scroll unrolled). What is named here is the bell's picture for an
            // install whose sprite cannot be read, at the skeleton's own seat and half its sheet size, and the
            // pointer's box round bell and scroll together (66 x 83 in the skeleton's resting pose).
            TownPanelButtons.Add(new TownPanelButtons.Entry
            {
                Id = BellId, Art = BellSheet + "#bell", Scale = 0.5f, Offset = new Vector2(1.75f, -2.5f), Size = new Vector2(42f, 81f), Box = new Vector2(66f, 83f), Place = 4,
                Name = () => TownEventText.Heading, Toggle = Toggle, IsOpen = () => IsOpen, ShownWhile = () => HasNotice
            });
        }

        // ---- when it shows -------------------------------------------------------------------------------

        private static void OnTick()
        {
            try
            {
                var active = TownEvents.Current;
                if (active == null || active.Shown || !active.Applied || IsOpen) return;
                // One thing at a time: the hamlet's report first, and never over a screen the player opened.
                if (!HamletScreen.IsOpen || NarrationBox.IsOpen || RosterWindow.IsOpen || QuestPanel.IsOpen || BuildingPanel.IsOpen) return;
                if (Open()) active.Shown = true;
            }
            catch (Exception e)
            {
                if (Time.unscaledTime < _nextComplaint) return;
                _nextComplaint = Time.unscaledTime + 10f;
                Plugin.Log.LogError("Town event notice failed: " + e);
            }
        }

        private static void OnChanged()
        {
            if (_open == null) return;
            var active = TownEvents.Current;
            if (active == null || active.Id != _open._eventId) Close();
            else _open.Refresh(active);
        }

        /// <summary>Shows this week's event; false when the week has none or the hamlet is not up.</summary>
        public static bool Open()
        {
            var active = TownEvents.Current;
            var canvas = RosterPanel.CanvasRoot;
            if (active == null || active.Def == null || canvas == null || !HamletScreen.IsOpen) return false;
            Close();
            RosterWindow.Close();

            // Framed as a building's window is (RosterWindow): the roster and the estate's bar stay as they are.
            // FALLBACK numbers: DD1's own values (the bar's top edge is 975 - 17 of the screen's 1080).
            var barTop = Dd1Ui.Offset(Dd1Ui.Layout(TownLayout), "town_screen_layout", "estate_summary_pos", 0f, 975f).y
                + Dd1Ui.Offset(Dd1Ui.Layout(SummaryLayout), "estate_summary_layout", "pos_offset", 0f, -17f).y;
            var root = UiKit.Rect("TownEvent", canvas);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = new Vector2(0f, Mathf.Max(0f, ScreenHeight - barTop));
            root.offsetMax = new Vector2(-RosterPanel.Width, 0f);
            root.gameObject.AddComponent<RectMask2D>();
            var panel = root.gameObject.AddComponent<TownEventPanel>();
            _open = panel;
            panel.Build(root, active);
            return true;
        }

        public static void Close()
        {
            if (_open == null) return;
            var go = _open.gameObject;
            _open = null;
            Destroy(go);
        }

        // ---- construction --------------------------------------------------------------------------------

        private static Vector2 Offset(DarkestFile file, string block, string key, float x, float y)
        {
            var found = file != null ? file.Find(block) : null;
            return found != null && found.Has(key) ? found.Vector2(key) : new Vector2(x, y);
        }

        private void Build(RectTransform root, TownEvents.Active active)
        {
            var def = active.Def;
            _eventId = active.Id;

            // A sibling of the frame, as in a building's window: a click on the darkened town closes the notice.
            var shade = UiKit.Image("Shade", root, null, HamletShade.Colour, true);
            UiKit.Stretch((RectTransform)shade.transform);
            shade.gameObject.AddComponent<TownEventPanelClose>();
            HamletShade.Attach(shade);

            var centre = new Vector2(0.5f, 0.5f);
            var corner = new Vector2(0f, 1f);
            var top = new Vector2(0.5f, 1f);
            // FALLBACK numbers below: DD1's own values of these entries, used when the layout file cannot be read.
            var layout = DarkestFile.Load(LayoutFile);
            _framePos = Offset(DarkestFile.Load(TownLayout), "town_screen_layout", "town_event_pos", Dd1Pos.x, Dd1Pos.y);
            _frame = UiKit.Rect("Frame", root).PlaceTopLeft(_framePos, corner, new Vector2(RosterWindow.FrameWidth, RosterWindow.FrameHeight));
            var backdrop = Dd1Install.Sprite(Art + "background.png");
            var back = UiKit.Image("Backdrop", _frame, backdrop, backdrop != null ? Color.white : new Color(0.05f, 0.05f, 0.055f, 0.97f), true);
            UiKit.Stretch((RectTransform)back.transform);
            back.gameObject.AddComponent<TownEventPanelClose>().RightClickOnly = true;

            var crier = Dd1Install.Sprite(Art + "character.png");
            if (crier != null)
            {
                var image = UiKit.Image("Crier", _frame, crier);
                ((RectTransform)image.transform).PlaceTopLeft(Offset(layout, "town_event_layout", "character_pos", -5f, 100f), corner, crier.rect.size);
            }

            // DD1's name of a town screen (town_event_layout name_pos is building_base_layout's name_pos seen
            // from the backdrop's corner): the crier's icon and "Hear Ye, Hear Ye!" between the two rules.
            Dd1ScreenName.Add(_frame, Offset(layout, "town_event_layout", "name_pos", -40f, -6f), Art + "icon.png", TownEventText.Heading);

            // DD1's close button where its layout puts it (town_event_layout close_pos).
            Dd1Ui.ArtButton("Close", _frame, "shared/progression/progression_close.png", Offset(layout, "town_event_layout", "close_pos", 1352f, 12f), CloseSize, Close, "X");

            // The scroll. DD1 measures the story from story_pos and the lower band from result_pos.
            var story = Offset(layout, "town_event_layout", "story_pos", 0f, 0f);
            const string storyLayout = "town_event_story_layout";
            _textWidth = layout?.Find(storyLayout)?.Float("description_text_width", 0, 485f) ?? 485f;

            var tone = Dd1Install.Sprite(Art + "tone_frame_" + (def.Tone ?? "neutral") + ".png") ?? Dd1Install.Sprite(Art + "tone_frame_neutral.png");
            if (tone != null)
            {
                var image = UiKit.Image("Tone", _frame, tone);
                ((RectTransform)image.transform).PlaceTopLeft(story + Offset(layout, storyLayout, "tone_frame_offset", 1030f, 115f), top, tone.rect.size);
            }

            // On the frame's upper band (dark): the title, in DD1's colour for the event's tone
            // (base.colours.darkest town_event_story_title_good / _bad / _neutral).
            Dd1Ui.Line("Title", _frame, "town_event_story_title", story + Offset(layout, storyLayout, "title_offset", 1030f, 150f), new Vector2(_textWidth, 40f), TextAlignmentOptions.Top,
                Dd1Fonts.Colour("town_event_story_title_" + (def.Tone ?? "neutral"), UiKit.Notable)).text = TownEventText.Title(def.Id);

            // an event of a DD1 feature keeps its picture under the feature's own folder (the districts' "Cornerstones")
            var art = Art + "image_" + def.Id + ".png";
            if (!Dd1Install.Exists(art) && def.Root.Length > 0) art = def.Root + art;
            var picture = Dd1Install.Exists(art) ? Dd1Install.Sprite(art) : null;
            if (picture != null)
            {
                var image = UiKit.Image("Picture", _frame, picture);
                ((RectTransform)image.transform).PlaceTopLeft(story + Offset(layout, storyLayout, "image_offset", 1030f, 216f), top, picture.rect.size);
            }

            // On the paper: the story, in ink.
            // DD1's style for it is Ubuntu in a near-black brown (town_event_story_description).
            Dd1Ui.Block("Description", _frame, "town_event_story_description", story + Offset(layout, storyLayout, "description_offset", 1030f, 480f), new Vector2(_textWidth, DescriptionHeight), TextAlignmentOptions.Top,
                Dd1Fonts.Colour("town_event_story_description", new Color32(21, 14, 5, 255)), 14f).text = TownEventText.Description(def.Id);

            // On the frame's lower band: what the event does, hung from DD1's first line (element_start_pos: the
            // middle of the line and its top), or (From Beyond) the fallen to choose from, a row of portraits
            // from the band's left (recruit_entry_start_pos, one every recruit_offset).
            _resultPos = Offset(layout, "town_event_layout", "result_pos", 770f, 610f);
            _infoStart = _resultPos + Offset(layout, "town_event_info_layout", "element_start_pos", 250f, 36f);
            const string result = "town_event_result_layout";
            _graveStart = _resultPos + Offset(layout, result, "recruit_entry_start_pos", 0f, 32f);
            _graveStep = Offset(layout, result, "recruit_offset", 100f, 0f);
            _graveArea = layout?.Find(result)?.Float("recruit_max_width", 0, 520f) ?? 520f;
            _graveFade = layout?.Find(result)?.Float("recruit_fade_out_time", 0, 1f) ?? 1f;
            var infoWidth = layout?.Find("town_event_info_layout")?.Float("element_text_width", 0, 485f) ?? 485f;
            _info = Dd1Ui.Block("Info", _frame, "town_event_info", _infoStart, new Vector2(infoWidth, InfoHeight), TextAlignmentOptions.Top, null, 13f);
            _graves = UiKit.Rect("Graves", _frame).PlaceTopLeft(_graveStart, corner, new Vector2(_graveArea, GraveSize.y));
            _tooltip = new Dd1Tooltip(_frame, RosterWindow.FrameWidth);

            Refresh(active);
        }

        private void Refresh(TownEvents.Active active)
        {
            // One of the fallen was taken: the others fade away (recruit_fade_out_time) before the band says what
            // the event did.
            if (_fadeAt >= 0f) return;
            _tooltip.Hide(null);
            if (active.Returned && _chosen != null && _graveRow.Count > 0)
            {
                _fadeAt = Time.unscaledTime;
                foreach (var grave in _graveRow)
                    if (grave.Value != null) grave.Value.blocksRaycasts = false;
                return;
            }
            for (var i = _graves.childCount - 1; i >= 0; i--) Destroy(_graves.GetChild(i).gameObject);
            _graveRow.Clear();
            var choosing = !active.Returned && active.Returnable.Count > 0;
            if (choosing)
            {
                var graves = new List<Graveyard.Fallen>();
                foreach (var key in active.Returnable)
                {
                    var fallen = TownEvents.FindFallen(key);
                    if (fallen != null) graves.Add(fallen);
                }
                choosing = graves.Count > 0;
                // DD1's row starts at the left of the band and steps to the right, closer together should the
                // row outgrow recruit_max_width.
                var step = graves.Count > 1 ? Mathf.Min(_graveStep.x, (_graveArea - GraveSize.x) / (graves.Count - 1)) : 0f;
                for (var i = 0; i < graves.Count; i++) AddGrave(graves[i], new Vector2(Mathf.Round(step * i), _graveStep.y * i));
            }
            _info.gameObject.SetActive(!choosing || _message != null);
            _graves.gameObject.SetActive(choosing && _message == null);
            _info.text = _message ?? string.Join("\n", TownEventText.Info(active));
        }

        // One of the fallen as DD1 shows a hero here: the portrait (DD2's face in DD1's hero slot, as everywhere
        // in the hamlet) inside DD1's frame for this screen. Who it is, the pointer is told in DD1's tooltip.
        private void AddGrave(Graveyard.Fallen fallen, Vector2 at)
        {
            var key = TownEvents.FallenKey(fallen);
            var slot = UpgradeUi.BuildSlot("Grave." + fallen.Name, _graves, at);
            slot.Show(HeroNames.Portrait(fallen.ClassId));
            Dd1Ui.Art("Frame", slot.Rect, GraveFrame, Vector2.zero, GraveSize);
            _graveRow.Add(new KeyValuePair<string, CanvasGroup>(key, slot.Back.gameObject.AddComponent<CanvasGroup>()));
            var corner = _graveStart + at;
            UpgradeUi.Pointer(slot.Back, () => Ask(fallen, key), Close,
                () => _tooltip.Show(slot, new Vector2(corner.x, corner.y - 2f), fallen.Name, HeroNames.ClassName(fallen.ClassId)),
                () => _tooltip.Hide(slot));
        }

        // DD1: only one of them can be returned to the living, so the choice is asked twice.
        private void Ask(Graveyard.Fallen fallen, string key)
        {
            var block = TownEvents.ReturnBlock(fallen);
            if (block != null)
            {
                Say(block);
                return;
            }
            // The mod's own question (DD1's string table has none for this choice), in DD1's dialog.
            TownConfirm.Ask("Return " + fallen.Name + " the " + HeroNames.ClassName(fallen.ClassId) + " to the living? Only one of the fallen can come back; the others stay where they lie.",
                "Return", "Not yet", () =>
                {
                    // known before the estate tells the notice that one has returned
                    _chosen = key;
                    var error = TownEvents.Return(key);
                    if (error == null) return;
                    _chosen = null;
                    if (_open == this) Say(error);
                });
        }

        // A line in place of the lower band's content until the next click on the scroll.
        private void Say(string message)
        {
            _message = message;
            var active = TownEvents.Current;
            if (active != null) Refresh(active);
        }

        private void Update()
        {
            Fade();
            // As a building's window: in the middle of the room left of the roster, shrinking with it on a narrow screen.
            var area = ((RectTransform)transform).rect;
            if (area.width <= 0f) return;
            var fit = Mathf.Min(1f, area.width / RoomWidth);
            var at = new Vector2(Mathf.Max(0f, (area.width - RoomWidth) * 0.5f) + _framePos.x * fit, -_framePos.y * fit);
            if (!Mathf.Approximately(fit, _frame.localScale.x)) _frame.localScale = new Vector3(fit, fit, 1f);
            if ((_frame.anchoredPosition - at).sqrMagnitude > 0.01f) _frame.anchoredPosition = at;
        }

        // DD1 lets the fallen who were not chosen fade out (recruit_fade_out_time, easeInSine).
        private void Fade()
        {
            if (_fadeAt < 0f) return;
            var share = _graveFade > 0f ? Mathf.Clamp01((Time.unscaledTime - _fadeAt) / _graveFade) : 1f;
            var left = Mathf.Cos(share * Mathf.PI * 0.5f);
            foreach (var grave in _graveRow)
                if (grave.Value != null && grave.Key != _chosen) grave.Value.alpha = left;
            if (share < 1f) return;
            _fadeAt = -1f;
            _chosen = null;
            var active = TownEvents.Current;
            if (active != null) Refresh(active);
        }

        internal void Clicked(PointerEventData click, bool rightClickOnly)
        {
            if (_message != null)
            {
                Say(null);
                return;
            }
            if (rightClickOnly && click.button != PointerEventData.InputButton.Right) return;
            Close();
        }

        // The hamlet is hidden by deactivating its canvas; the notice does not wait for it to come back.
        private void OnDisable()
        {
            if (_open == this) _open = null;
            Destroy(gameObject);
        }

        // ---- dev -----------------------------------------------------------------------------------------

        public static object Snapshot()
        {
            if (_open == null) return new { open = false };
            return new { open = true, id = _open._eventId, info = _open._info.text, choosing = _open._graves.gameObject.activeSelf, message = _open._message };
        }
    }

    /// <summary>Closes the notice: any click on the darkened town around it, a right click on the notice itself (DD1's way out of a screen).</summary>
    internal class TownEventPanelClose : MonoBehaviour, IPointerClickHandler
    {
        public bool RightClickOnly;

        public void OnPointerClick(PointerEventData eventData)
        {
            var panel = GetComponentInParent<TownEventPanel>();
            if (panel != null) panel.Clicked(eventData, RightClickOnly);
        }
    }
}
