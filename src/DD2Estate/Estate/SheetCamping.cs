using System;
using System.Collections.Generic;
using System.Reflection;
using Assets.Code.Actor;
using Assets.Code.Audio;
using Assets.Code.Audio.Sfx;
using Assets.Code.Data;
using Assets.Code.Library;
using Assets.Code.Locale;
using Assets.Code.UI;
using Assets.Code.UI.Tooltips;
using Assets.Code.UI.Widgets;
using Assets.Code.Utils;
using BepInEx.Configuration;
using DD2Estate.Core;
using DD2Estate.Dd1;
using DD2Estate.Dd2;
using DD2Estate.Dev;
using DD2Estate.Dungeon;
using FMODUnity;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DD2Estate.Estate
{
    /// <summary>
    /// The hero's camping skills on DD2's character sheet: where the sheet shows the combat skills there is a
    /// switch, "Combat Skills" / "Camping Skills" (DD1's own words: character_title_combat_skills,
    /// character_title_camping_skills), and the camping view shows the camping skills the hero has learnt
    /// (<see cref="Survivalist"/>): the class's own first, the shared ones after them, in rows no wider than
    /// the sheet's own grid of combat skills (the row begins anew there: six to a row at DD1's size). What the
    /// hero has not learnt is not shown here, and no price: learning is the Survivalist's business, and that
    /// screen shows both.
    ///
    /// Everything on it is DD2's own, taken from the sheet it sits in: the switch is a copy of the sheet's
    /// "Skill Loadouts" control (a diamond button with a label, at the foot of the skills panel; DD2 shows it
    /// only in its own expeditions, so its place is free in the Estate), a skill is a copy of the sheet's skill
    /// button (its frame of a chosen skill with the number in the corner, its grey for a skill that is not
    /// chosen, its hover frame, its sounds, its tooltip box), under a line set like the sheet's "Rank" line.
    /// The pictures are DD1's (raid/camping/skill_icons, 72x72), the words DD1's (<see cref="CampText"/>).
    ///
    /// The pictures stand at DD1's own size, as on the Survivalist's screen: 72 pixels of DD1's 1920x1080
    /// screen (<see cref="SheetCampingView.Dd1Icon"/>). DD2's button draws its picture 121 units wide in a cell
    /// of 116, which blew DD1's small pictures up to more than twice their size and made them soft; so the
    /// button, whole, is made as much smaller as brings the picture in it to 72 (its frame, its numbered
    /// corner and its hover frame with it, in DD2's own proportions).
    ///
    /// In the hamlet a click on a skill makes it ready for the next expedition or puts it aside: the same rule
    /// and the same state as at the Survivalist (<see cref="Survivalist.Toggle"/>). On an expedition the view is
    /// for looking only. A hero who has learnt none is shown one line that says who teaches them.
    /// </summary>
    [EstateModule]
    internal static class SheetCamping
    {
        private static ConfigEntry<bool> _enabled;

        private static void Register()
        {
            _enabled = Plugin.Settings.Bind("Sheet", "CampingSkills", true,
                "The hero's sheet in the Estate has a switch from its combat skills to the hero's camping skills, which are chosen there while in the hamlet.");
            // The view of the open sheet: {"on":true} the camping skills, {"on":false} the combat skills; no "on": as it stands.
            AgentBridge.Register("sheet.camping", o =>
            {
                var view = Open();
                if (view == null) return "the sheet is not open (or the camping view is switched off)";
                if (o["on"] != null) view.Show((bool)o["on"]);
                return view.Describe();
            });
            // A click on a skill of the camping view: {"skill":"encourage"}.
            AgentBridge.Register("sheet.campskill", o =>
            {
                var view = Open();
                if (view == null) return "the sheet is not open (or the camping view is switched off)";
                if (!view.Camping) view.Show(true);
                var said = view.Press((string)o["skill"]);
                return new { said, view = view.Describe() };
            });
            // For a look at the view of a hero who knows fewer skills (every recruit arrives knowing three):
            // {"guid":3,"forget":"all"} or {"guid":3,"forget":"encourage"} takes skills out of the hero's book.
            // A test estate's matter: the book is saved as it stands with the estate's next save.
            AgentBridge.Register("sheet.campbook", o =>
            {
                var hero = UpgradeUi.Hero((uint?)o["guid"] ?? 0u);
                if (hero == null) return "no such hero";
                var book = Survivalist.Book(hero);
                var forget = (string)o["forget"];
                if (forget == "all")
                {
                    book.Known.Clear();
                    book.Active.Clear();
                }
                else if (!string.IsNullOrEmpty(forget))
                {
                    book.Known.Remove(forget);
                    book.Active.Remove(forget);
                }
                var view = Open();
                if (view != null && view.Camping) view.Show(true);
                return new { hero = hero.ActorName, cls = book.ClassId, known = book.Known, ready = book.Active };
            });
        }

        public static bool Enabled => _enabled != null && _enabled.Value;

        private static SheetCampingView Open()
        {
            var sheet = SheetCosmeticsDev.Sheet();
            return sheet != null ? sheet.GetComponent<SheetCampingView>() : null;
        }

        /// <summary>The sheet is being opened: in the Estate it gets (or keeps) the switch, elsewhere it is DD2's own again.</summary>
        public static void Opening(CharacterSheetUiBhv sheet)
        {
            if (sheet == null) return;
            var view = sheet.GetComponent<SheetCampingView>();
            if (!EstateSession.Active || !Enabled)
            {
                if (view != null) view.Dormant();
                return;
            }
            if (view == null) view = sheet.gameObject.AddComponent<SheetCampingView>();
            view.Wake(sheet);
        }

        public static void Populated(CharacterSheetStatsUiBhv stats, ActorInstance actor)
        {
            if (stats == null || actor == null) return;
            var view = stats.GetComponentInParent<SheetCampingView>();
            if (view != null) view.HeroShown(actor.ActorGuid);
        }
    }

    [HarmonyPatch(typeof(CharacterSheetUiBhv), nameof(CharacterSheetUiBhv.OnScreenOpenStart))]
    internal static class SheetOpensWithCampingSwitch
    {
        private static void Prefix(CharacterSheetUiBhv __instance)
        {
            try { SheetCamping.Opening(__instance); }
            catch (Exception e) { Plugin.Log.LogError("Sheet: the camping view could not be set up: " + e); }
        }
    }

    [HarmonyPatch(typeof(CharacterSheetStatsUiBhv), nameof(CharacterSheetStatsUiBhv.Populate))]
    internal static class SheetShowsHeroesCampingSkills
    {
        private static void Postfix(CharacterSheetStatsUiBhv __instance, ActorInstance actor)
        {
            try { SheetCamping.Populated(__instance, actor); }
            catch (Exception e) { Plugin.Log.LogError("Sheet: the camping skills could not be shown: " + e); }
        }
    }

    /// <summary>The camping view of one character sheet (DD2 keeps a sheet for the mouse and one for the pad).</summary>
    internal class SheetCampingView : MonoBehaviour
    {
        private const string CampfireArt = "campaign/town/buildings/camping_trainer/camping_trainer.cost.icon.png";
        private const float MessageSeconds = 3f;
        /// <summary>
        /// A camping skill's picture as DD1 draws it where the skills are trained (the Survivalist's grid: the
        /// 72x72 art at its own size), in pixels of DD1's 1920x1080 screen.
        /// </summary>
        public const float Dd1Icon = 72f;
        // Between two pictures. DD2's grid leaves 19 units between two pictures of 121 (cells of 116, 24 apart);
        // at this size that would be 11: a little more air, since nothing but the pictures tells them apart here.
        private const float Gap = 16f;
        // FALLBACK: how wide the sheet's grid of combat skills is (four cells of 116, 24 apart), when the grid
        // cannot be asked. The camping skills keep inside that width: the panel's frame and the hero's seal
        // stand right of it (SEEN: a seventh picture in the row ran under the seal, past the panel's edge).
        private const float Dd2GridWidth = 4f * 116f + 3f * 24f;
        // The numbered corner of a ready skill, larger than the rest of the button by this much.
        private const float NumberGrown = 1.3f;
        // FALLBACK: the sheet's own cell of a skill button (its grid's cellSize), when the grid cannot be asked.
        private static readonly Vector2 Dd2Button = new Vector2(116f, 116f);
        private static readonly Color Gold = new Color32(0xB0, 0x93, 0x5E, 0xFF);

        private static Sprite _campfire;

        private CharacterSheetUiBhv _sheet;
        private bool _built, _failed;
        private GameObject _skillsPanel, _launch, _target, _buttons, _switch, _camp, _cellPrefab;
        private RectTransform _grid;
        private GridLayoutGroup _layout;
        private Vector2 _buttonSize = Dd2Button;
        private float _gridWidth = Dd2GridWidth;
        private TextMeshProUGUI _title, _switchLabel, _info;
        private Image _switchIcon;
        private Sprite _combatIcon;
        private Material _grey;
        private int _hidden;
        private float _unit = 1f;
        private uint _guid;
        private string _shown, _titleOfCombat, _message;
        private float _messageUntil;
        private readonly List<SheetCampCell> _cells = new List<SheetCampCell>();

        /// <summary>The camping skills are shown in place of the combat skills.</summary>
        public bool Camping { get; private set; }

        private static CampingRules Rules => CampContent.Rules;

        private static string CampingWords => Dd1Strings.Get("character_title_camping_skills") ?? "Camping Skills";

        private static string CombatWords => Dd1Strings.Get("character_title_combat_skills") ?? "Combat Skills";

        // ---- the sheet opens and closes ----------------------------------------------------------------

        public void Wake(CharacterSheetUiBhv sheet)
        {
            _sheet = sheet;
            if (!_built && !_failed)
            {
                try { Build(); }
                catch (Exception e)
                {
                    _failed = true;
                    Plugin.Log.LogError("Sheet: the camping view could not be built on DD2's sheet (the sheet stays as DD2 has it): " + e);
                }
            }
            if (!_built) return;
            Camping = false;
            Apply();
            _switch.SetActive(true);
        }

        /// <summary>Outside the Estate: the sheet is DD2's own, nothing of the camping view shows.</summary>
        public void Dormant()
        {
            if (!_built) return;
            Camping = false;
            Apply();
            _switch.SetActive(false);
        }

        private void OnEnable()
        {
            Survivalist.Changed += OnBookChanged;
        }

        private void OnDisable()
        {
            // (the sheet has closed; the next one opens on the combat skills: Wake and Dormant see to it)
            Survivalist.Changed -= OnBookChanged;
        }

        private void OnBookChanged()
        {
            if (_built && Camping) Refresh(true);
        }

        public void HeroShown(uint guid)
        {
            _guid = guid;
            if (_built && Camping) Refresh(true);
        }

        // ---- building, once a sheet -------------------------------------------------------------------

        private static T Field<T>(object of, string name) where T : class
        {
            return AccessTools.Field(of.GetType(), name)?.GetValue(of) as T;
        }

        private void Build()
        {
            var stats = Field<CharacterSheetStatsUiBhv>(_sheet, "m_characterSheetStatsBhv");
            var loadout = Field<GameObject>(_sheet, "m_skillLoadoutButtonContainer");
            var buttons = Field<Transform>(stats, "m_combatSkillsContainer");
            var pool = Field<Assets.Code.CommonLogic.Pooling.GameObjectPoolBhv>(stats, "m_combatSkillsPoolBhv");
            var launch = Field<Component>(stats, "m_combatSkillsAverageLaunchPipsPooledListBhv");
            var target = Field<Component>(stats, "m_combatSkillsAverageTargetPipsPooledListBhv");
            _skillsPanel = _sheet.GetTabPanel(CharacterSheetUiBhv.Tab.Skills);
            var title = _sheet.transform.Find("StatsPanel/Title");
            if (stats == null || loadout == null || buttons == null || pool == null || pool.prefab == null || launch == null || target == null || _skillsPanel == null || title == null)
                throw new InvalidOperationException("DD2's sheet is not laid out as expected");
            _title = title.GetComponent<TextMeshProUGUI>();
            _buttons = buttons.gameObject;
            _launch = launch.transform.parent.gameObject;
            _target = target.transform.parent.gameObject;
            _cellPrefab = pool.prefab;
            var skillButton = _cellPrefab.GetComponent<CharacterSheetSkillButtonBhv>();
            _grey = skillButton != null ? Field<Material>(skillButton, "m_greyscaleMaterial") : null;
            foreach (var image in _sheet.GetComponentsInChildren<Image>(true))
                if (_combatIcon == null && image.sprite != null && image.sprite.name == "icon_stats") _combatIcon = image.sprite;

            // the camping view: a line like the "Rank" line and a row of pictures where the combat skills' grid stands, in the same panel
            var panel = buttons.parent;
            _camp = new GameObject("CampingSkills", typeof(RectTransform));
            _camp.SetActive(false);
            var camp = (RectTransform)_camp.transform;
            camp.SetParent(panel, false);
            camp.anchorMin = Vector2.zero;
            camp.anchorMax = Vector2.one;
            camp.offsetMin = camp.offsetMax = Vector2.zero;

            // (the sheet slides these parts in from the left as it opens, and it may be opening right now: where
            // they stand across is where they rest, at the panel's own left edge; only their height is taken)
            var line = Instantiate(_launch, camp, false);
            line.name = "ReadyLine";
            var lineRect = (RectTransform)line.transform;
            lineRect.anchoredPosition = new Vector2(0f, ((RectTransform)_launch.transform).anchoredPosition.y);
            for (var i = line.transform.childCount - 1; i >= 0; i--)
            {
                var child = line.transform.GetChild(i);
                var label = child.GetComponent<TextMeshProUGUI>();
                if (label != null && _info == null)
                {
                    _info = label;
                    // DD2's label is as wide as its word ("Rank") and no wider than a limit; this one holds a line
                    foreach (var behaviour in child.GetComponents<MonoBehaviour>())
                        if (!(behaviour is TextMeshProUGUI)) DestroyImmediate(behaviour);
                    continue;
                }
                DestroyImmediate(child.gameObject);
            }
            if (_info == null) throw new InvalidOperationException("the \"Rank\" line has no label");
            foreach (var group in line.GetComponents<LayoutGroup>()) DestroyImmediate(group);
            _info.name = "Ready";
            var size = _info.enableAutoSizing ? _info.fontSizeMax : _info.fontSize;
            _info.enableAutoSizing = false;
            _info.fontSize = size;
            _info.richText = true;
            _info.raycastTarget = false;
            _info.textWrappingMode = TextWrappingModes.NoWrap;
            _info.overflowMode = TextOverflowModes.Overflow;
            _info.alignment = TextAlignmentOptions.Left;
            var infoRect = _info.rectTransform;
            infoRect.anchorMin = Vector2.zero;
            infoRect.anchorMax = Vector2.one;
            infoRect.pivot = new Vector2(0f, 0.5f);
            infoRect.offsetMin = infoRect.offsetMax = Vector2.zero;

            var grid = new GameObject("Skills", typeof(RectTransform));
            _grid = (RectTransform)grid.transform;
            _grid.SetParent(camp, false);
            var from = (RectTransform)buttons;
            _grid.anchorMin = from.anchorMin;
            _grid.anchorMax = from.anchorMax;
            _grid.pivot = from.pivot;
            _grid.anchoredPosition = new Vector2(0f, from.anchoredPosition.y);
            _grid.sizeDelta = from.sizeDelta;
            // DD2's grid begins at the panel's upper left corner and so does this one; its cells are the pictures
            // themselves (their size is set whenever the skills are shown: Refresh)
            var theirs = buttons.GetComponent<GridLayoutGroup>();
            if (theirs != null && theirs.cellSize.x > 1f && theirs.cellSize.y > 1f) _buttonSize = theirs.cellSize;
            // as wide as DD2 lays its own skills out (the container's own box is wider than the panel)
            if (theirs != null && theirs.constraint == GridLayoutGroup.Constraint.FixedColumnCount && theirs.constraintCount > 0 && theirs.cellSize.x > 1f)
                _gridWidth = theirs.constraintCount * theirs.cellSize.x + (theirs.constraintCount - 1) * theirs.spacing.x;
            _layout = grid.AddComponent<GridLayoutGroup>();
            if (theirs != null) _layout.padding = theirs.padding;
            _layout.childAlignment = TextAnchor.UpperLeft;
            _layout.startCorner = GridLayoutGroup.Corner.UpperLeft;
            _layout.startAxis = GridLayoutGroup.Axis.Horizontal;
            _layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            _layout.constraintCount = PerRow(1f);

            // the switch: DD2's "Skill Loadouts" control, in its place
            _switch = Instantiate(loadout, loadout.transform.parent, false);
            _switch.name = "CampingSwitch";
            _switch.SetActive(false);
            var button = _switch.GetComponentInChildren<Button>(true);
            _switchLabel = _switch.GetComponentInChildren<TextMeshProUGUI>(true);
            if (button == null || _switchLabel == null) throw new InvalidOperationException("the \"Skill Loadouts\" control has no button or no label");
            foreach (var behaviour in _switchLabel.GetComponents<MonoBehaviour>())
                if (!(behaviour is TextMeshProUGUI)) DestroyImmediate(behaviour);       // DD2's own words for it
            // the copy carries DD2's click (the loadout screen): a new event, to which the button's own sound is added at its start
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(Flip);
            foreach (var image in button.GetComponentsInChildren<Image>(true))
                if (image.gameObject != button.gameObject) _switchIcon = image;
            _switchLabel.raycastTarget = true;
            _switchLabel.textWrappingMode = TextWrappingModes.NoWrap;
            _switchLabel.overflowMode = TextOverflowModes.Overflow;
            var word = _switchLabel.gameObject.AddComponent<SheetCampSwitchWord>();
            word.View = this;
            word.Label = _switchLabel;
            word.Rest = _switchLabel.color;
            word.Over = button.colors.highlightedColor;

            _built = true;
        }

        // ---- combat skills or camping skills -----------------------------------------------------------

        public void Flip()
        {
            Show(!Camping);
            Play(AudioPathsBhv.MinorClick);
        }

        public void Show(bool camping)
        {
            if (!_built) return;
            Camping = camping;
            Apply();
        }

        private void Apply()
        {
            _launch.SetActive(!Camping);
            _target.SetActive(!Camping);
            _buttons.SetActive(!Camping);
            _camp.SetActive(Camping);
            _switchLabel.text = Camping ? CombatWords : CampingWords;
            if (_switchIcon != null)
            {
                var icon = Camping ? _combatIcon : Campfire();
                if (icon != null)
                {
                    _switchIcon.sprite = icon;
                    _switchIcon.preserveAspect = true;
                }
            }
            if (Camping)
            {
                if (_guid == 0u) _guid = _sheet.ActorGuid;
                Refresh(true);
            }
            else if (_title != null && _titleOfCombat != null && _title.text == CampingWords) _title.text = _titleOfCombat;
            _message = null;
        }

        private void LateUpdate()
        {
            if (!_built || !Camping) return;
            // (a timeline of the sheet's that shows the tab again may switch DD2's own parts back on)
            if (_buttons.activeSelf) _buttons.SetActive(false);
            if (_launch.activeSelf) _launch.SetActive(false);
            if (_target.activeSelf) _target.SetActive(false);
            // the sheet's title is the tab's name, set by DD2 whenever a tab is shown: over the camping skills it says so
            if (_title != null && _skillsPanel.activeInHierarchy)
            {
                var words = CampingWords;
                if (_title.text != words)
                {
                    _titleOfCombat = _title.text;
                    _title.text = words;
                }
            }
            if (_message != null && Time.unscaledTime >= _messageUntil)
            {
                _message = null;
                WriteInfo();
            }
            if (_sheet != null && _sheet.ActorGuid != _guid)
            {
                _guid = _sheet.ActorGuid;
                Refresh(true);
            }
            // the window took another shape: the pictures are DD1's size on the screen as it is now
            else if (Mathf.Abs(Dd1Pixel - _unit) > 0.002f) Refresh(true);
        }

        // ---- the hero's book --------------------------------------------------------------------------

        private void Refresh(bool force)
        {
            var unit = _unit = Dd1Pixel;
            var hero = UpgradeUi.Hero(_guid);
            if (hero == null)
            {
                Clear();
                _info.text = "";
                _shown = null;
                return;
            }
            var book = Survivalist.Book(hero);
            var key = new System.Text.StringBuilder().Append(_guid).Append('|').Append(unit.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)).Append('|').Append(CanChoose ? 'h' : 'x');
            foreach (var skill in Rules.SkillsFor(book.ClassId))
                key.Append('|').Append(skill.Id).Append(book.Knows(skill.Id) ? 'k' : '-').Append(book.Active.IndexOf(skill.Id));
            if (!force && key.ToString() == _shown) return;
            _shown = key.ToString();

            Clear();
            _layout.cellSize = new Vector2(Dd1Icon * unit, Dd1Icon * unit);
            _layout.spacing = new Vector2(Gap * unit, Gap * unit);
            _layout.constraintCount = PerRow(unit);
            var resource = Singleton<ResourceDatabaseActors>.Instance.GetResource(hero.ActorDataId, isErrorValid: false);
            var background = resource != null ? resource.GetTopMostParent().m_SkillIconBG : null;
            // what the hero has learnt, the class's own first: the order of the Survivalist's two grids
            _hidden = 0;
            foreach (var skill in Rules.ClassSkills(book.ClassId)) AddLearnt(book, skill, background, unit);
            foreach (var skill in Rules.SharedSkills()) AddLearnt(book, skill, background, unit);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_grid);
            WriteInfo();
        }

        private void AddLearnt(CampSkillBook book, CampSkill skill, Sprite background, float unit)
        {
            if (book.Knows(skill.Id)) AddCell(book, skill, background, unit);
            else _hidden++;
        }

        // As many pictures to a row as stand side by side, with their gaps, in the width of the sheet's own grid
        // of skills; the next begins a new row (six at DD1's size on a 16:9 screen, fewer where the sheet is
        // drawn smaller against the mod's pixels).
        private int PerRow(float unit)
        {
            float cell = Dd1Icon * unit, gap = Gap * unit;
            return Mathf.Max(1, Mathf.FloorToInt((_gridWidth + gap + 0.5f) / (cell + gap)));
        }

        // How many of the sheet's own units one pixel of DD1's 1920x1080 screen is (the mod's canvases are that
        // screen, fitted to the display's height; DD2's sheet is on a canvas of DD2's, scaled DD2's way: on a
        // 16:9 display the two are the same, and this is 1).
        private float Dd1Pixel
        {
            get
            {
                var sheet = _grid != null ? _grid.lossyScale.x : 0f;
                return sheet > 0.0001f ? Screen.height / 1080f / sheet : 1f;
            }
        }

        private void Clear()
        {
            _cells.Clear();
            for (var i = _grid.childCount - 1; i >= 0; i--)
            {
                var old = _grid.GetChild(i).gameObject;
                old.SetActive(false);
                Destroy(old);
            }
        }

        /// <summary>The ready skills can be changed here and now: in the hamlet.</summary>
        private static bool CanChoose => EstateSession.InHub && EstateSession.View == EstateSession.Screen.Hamlet;

        private void WriteInfo()
        {
            if (_info == null) return;
            if (_message != null)
            {
                _info.text = "<color=#" + ColorUtility.ToHtmlStringRGB(SheetColours.Harm) + ">" + _message + "</color>";
                return;
            }
            var hero = UpgradeUi.Hero(_guid);
            if (hero == null)
            {
                _info.text = "";
                return;
            }
            var book = Survivalist.Book(hero);
            // nothing learnt (or a class DD1 has no camping skills for): one quiet line in the line's own grey
            if (book.Known.Count == 0)
            {
                _info.text = "<size=80%>" + (Rules.SkillsFor(book.ClassId).Count == 0 ? "This class has no camping skills." : "No camping skills learnt yet: the Survivalist teaches them.") + "</size>";
                return;
            }
            _info.text = "Ready <color=#" + ColorUtility.ToHtmlStringRGB(Gold) + ">" + book.Active.Count + "</color> of " + Rules.ActiveLimit;
        }

        private void Say(string message)
        {
            _message = message;
            _messageUntil = Time.unscaledTime + MessageSeconds;
            WriteInfo();
        }

        // One learnt skill: DD2's skill button, whole, in a cell of the picture's size.
        private void AddCell(CampSkillBook book, CampSkill skill, Sprite background, float unit)
        {
            // the grid's cell is the picture's own square; the button stands in its middle
            var holder = new GameObject("Cell." + skill.Id, typeof(RectTransform));
            holder.transform.SetParent(_grid, false);
            // DD2's template is switched off, so nothing of the copy has woken yet when its parts are taken out
            var awake = _cellPrefab.activeSelf;
            if (awake) _cellPrefab.SetActive(false);
            var go = Instantiate(_cellPrefab, holder.transform, false);
            if (awake) _cellPrefab.SetActive(true);
            go.name = "CampSkill." + skill.Id;
            // at DD2's own size (its grid's cell), which its parts are laid out for ...
            var button = (RectTransform)go.transform;
            button.anchorMin = button.anchorMax = button.pivot = new Vector2(0.5f, 0.5f);
            button.anchoredPosition = Vector2.zero;
            button.sizeDelta = _buttonSize;
            // (the copy's click is DD2's, bound to the part that goes: a new one, to which the button's own sound adds itself)
            var click = go.GetComponent<Button>();
            if (click != null) click.onClick = new Button.ButtonClickedEvent();
            // what makes it a combat skill's button goes; its frame, pictures, sounds and tooltip box stay
            foreach (var behaviour in go.GetComponents<MonoBehaviour>())
            {
                if (behaviour is DataContextBhv || behaviour is TextTooltipBhv || behaviour is ButtonAudioBhv || behaviour is Selectable) continue;
                DestroyImmediate(behaviour);
            }
            foreach (var animator in go.GetComponents<Animator>()) animator.enabled = false;
            foreach (var director in go.GetComponents<UnityEngine.Playables.PlayableDirector>())
            {
                director.playOnAwake = false;
                director.enabled = false;
            }

            var order = book.Active.IndexOf(skill.Id);
            var cell = go.AddComponent<SheetCampCell>();
            cell.View = this;
            cell.SkillId = skill.Id;
            cell.Ready = order >= 0;
            cell.Hover = Child(go, "overly_hovered_image");
            _cells.Add(cell);

            Off(go, "NotificationIcon");
            Off(go, "ButtonPrompt");
            Off(go, "CooldownText");
            Off(go, "HotkeyText");
            Off(go, "selection_vfx_anchor");
            Off(go, "overly_hovered_image");
            var chosen = Child(go, "overlay_selected_image");
            if (chosen != null)
            {
                chosen.SetActive(order >= 0);
                // The number's corner is made smaller with the button, and its digit with it (SEEN: some 11 px
                // tall on a 1440 screen, dark on gold: hard to read). It grows a little again, about the
                // frame's own corner, which it stays in.
                if (chosen.transform.Find("OrderContainer") is RectTransform corner)
                {
                    var upperLeft = new Vector2(0f, 1f);
                    corner.anchoredPosition += Vector2.Scale(upperLeft - corner.pivot, corner.rect.size);
                    corner.pivot = upperLeft;
                    corner.localScale = new Vector3(NumberGrown, NumberGrown, 1f);
                }
            }

            var icon = CampContent.Icon(skill);
            RectTransform picture = null;
            foreach (var image in go.GetComponentsInChildren<Image>(true))
            {
                if (image.name == "SkillIcon")
                {
                    // DD1's picture fills DD2's place for a skill's picture; DD2's own picture is bound to the cell's data
                    foreach (var bound in image.GetComponents<MonoBehaviour>())
                        if (!(bound is Image)) DestroyImmediate(bound);
                    image.sprite = icon;
                    image.overrideSprite = null;
                    image.enabled = icon != null;
                    // a skill that is not ready is grey, as DD2's sheet has a skill that is not chosen
                    image.material = order >= 0 || _grey == null ? null : _grey;
                    image.color = Color.white;
                    picture = image.rectTransform;
                }
                else if (image.name == "BG")
                {
                    // the class's ground of a skill picture: DD1's pictures are whole squares and cover it; it
                    // shows only where DD1's picture is missing
                    foreach (var bound in image.GetComponents<MonoBehaviour>())
                        if (!(bound is Image)) DestroyImmediate(bound);
                    image.sprite = background;
                    image.overrideSprite = null;
                    image.enabled = background != null && icon == null;
                    image.material = order >= 0 || _grey == null ? null : _grey;
                    image.color = Color.white;
                }
            }
            cell.Picture = picture;
            foreach (var text in go.GetComponentsInChildren<TextMeshProUGUI>(true))
                if (text.name == "NumberLabel")
                {
                    foreach (var bound in text.GetComponents<MonoBehaviour>())
                        if (!(bound is TextMeshProUGUI)) DestroyImmediate(bound);
                    text.text = order >= 0 ? (order + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
                }

            var tip = go.GetComponent<TextTooltipBhv>();
            go.SetActive(true);
            // ... and as much smaller as brings the picture in it to DD1's size (DD2 draws it 121 wide in its cell of 116)
            var drawn = picture != null ? picture.rect.width : 0f;
            var scale = Dd1Icon * unit / (drawn > 1f ? drawn : _buttonSize.x);
            button.localScale = new Vector3(scale, scale, 1f);
            if (tip != null)
            {
                tip.enabled = true;
                // DD2 hangs the box beside the button, its middle level with the button's: beside a picture this
                // small it lay over the "Ready" line above. It hangs from the picture's top, where DD1 hangs a
                // skill's tooltip at the Survivalist (icon_tooltip_offset: to the right, level with the top).
                tip.SetTooltipPivotY(1f);
                tip.SetTooltipNormalizedY(1f);
                var words = Tooltip(skill);
                tip.AddPostStartCallback(() => tip.Populate(words));
            }
        }

        // DD2's tooltip box, set as DD2 sets an item's: the name in its title face and colour, then DD1's lines.
        private static string Tooltip(CampSkill skill)
        {
            var name = CampText.Name(skill);
            string title;
            try { title = Singleton<Localization>.Instance.GetSubstitutedText("<size=120%><font=\"NDDunkelD-Bold SDF\"><color=#{item_nameline}>" + name + "</color></font></size>"); }
            catch (Exception) { title = name; }
            return title + "\n" + CampText.Cost(skill) + "\n" + CampText.Describe(skill);
        }

        private static GameObject Child(GameObject of, string name)
        {
            var child = of.transform.Find(name);
            return child != null ? child.gameObject : null;
        }

        private static void Off(GameObject of, string name)
        {
            var child = Child(of, name);
            if (child != null) child.SetActive(false);
        }

        private static void Play(EventReference sound)
        {
            try
            {
                if (!sound.IsNull && SingletonMonoBehaviour<AudioMgr>.HasInstance()) SingletonMonoBehaviour<AudioMgr>.Instance.Play(sound);
            }
            catch (Exception) { }
        }

        // ---- a click on a skill -------------------------------------------------------------------------

        /// <summary>A click on a skill of the view. Returns what was said (null: the skill changed places quietly).</summary>
        public string Press(string skillId)
        {
            var hero = UpgradeUi.Hero(_guid);
            var skill = Rules.Skill(skillId);
            if (hero == null || skill == null) return "no such skill";
            var book = Survivalist.Book(hero);
            string trouble;
            if (!book.Knows(skillId)) trouble = "Not learnt: the Survivalist teaches it";
            else
            {
                var was = book.IsActive(skillId);
                trouble = Survivalist.Toggle(_guid, skillId);
                if (trouble == null)
                {
                    _message = null;
                    Play(was ? AudioPathsBhv.SkillUnequip : AudioPathsBhv.SkillEquip);
                    Refresh(true);      // (Survivalist.Changed has done it already; this is for a handler that failed)
                    return null;
                }
            }
            Play(AudioPathsBhv.ClickInvalid);
            trouble = trouble.TrimEnd('.');
            Say(trouble + ".");
            return trouble;
        }

        public void Hovered(SheetCampCell cell, bool over)
        {
            if (cell != null && cell.Hover != null) cell.Hover.SetActive(over);
        }

        // ---- the campfire of the switch ----------------------------------------------------------------

        // DD2's sign in its diamond buttons is one-coloured (white, tinted by the button). DD1's campfire (the
        // Survivalist's cost icon) is made one for it: its light becomes its shape.
        private static Sprite Campfire()
        {
            if (_campfire != null) return _campfire;
            try
            {
                if (!Dd1Install.Found || !Dd1Install.Exists(CampfireArt)) return null;
                var texture = Dd1Install.LoadTexture(CampfireArt, true, false);
                if (texture == null) return null;
                var pixels = texture.GetPixels32();
                for (var i = 0; i < pixels.Length; i++)
                {
                    var p = pixels[i];
                    var light = Mathf.Max(p.r, Mathf.Max(p.g, p.b)) / 255f;
                    var shape = Mathf.Clamp01((light - 0.16f) / 0.5f);
                    pixels[i] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(shape * p.a));
                }
                texture.SetPixels32(pixels);
                texture.Apply(false, true);
                _campfire = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                _campfire.name = "estate_campfire_sign";
                _campfire.hideFlags = HideFlags.HideAndDontSave;
            }
            catch (Exception e) { Plugin.Log.LogWarning("Sheet: DD1's campfire could not be made a sign: " + e.Message); }
            return _campfire;
        }

        // ---- dev bridge ----------------------------------------------------------------------------------

        public object Describe()
        {
            if (!_built) return new { built = false, failed = _failed };
            var hero = UpgradeUi.Hero(_guid != 0u ? _guid : _sheet.ActorGuid);
            var book = hero != null ? Survivalist.Book(hero) : null;
            var cells = new List<object>();
            foreach (var cell in _cells)
            {
                if (cell == null) continue;
                var skill = Rules.Skill(cell.SkillId);
                var rect = (RectTransform)cell.transform;
                var corners = new Vector3[4];
                rect.GetWorldCorners(corners);
                var canvas = rect.GetComponentInParent<Canvas>();
                var camera = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
                var centre = RectTransformUtility.WorldToScreenPoint(camera, (corners[0] + corners[2]) * 0.5f);
                cells.Add(new
                {
                    id = cell.SkillId, name = CampText.Name(skill), shared = Rules.IsShared(skill), ready = cell.Ready,
                    order = book != null ? book.Active.IndexOf(cell.SkillId) + 1 : 0,
                    x = Mathf.RoundToInt(centre.x), y = Mathf.RoundToInt(centre.y),
                    // boxes on DD1's 1920x1080 screen ("x,y wxh", y down): the picture should be 72 wide there
                    picture = cell.Picture != null ? SheetEquipment.Box(cell.Picture) : null, button = SheetEquipment.Box(rect), scale = rect.localScale.x
                });
            }
            return new
            {
                built = true, view = Camping ? "camping" : "combat", canChoose = CanChoose,
                title = _title != null ? _title.text : null, switchWords = _switchLabel.text, switchShown = _switch.activeInHierarchy,
                hero = hero != null ? hero.ActorName : null, cls = book != null ? book.ClassId : null,
                learnt = book != null ? book.Known.Count : 0, notShown = _hidden,
                ready = book != null ? book.Active.Count : 0, limit = Rules.ActiveLimit, line = _info != null ? _info.text : null,
                combatShown = _buttons.activeSelf, dd1Pixel = Dd1Pixel, cell = _layout != null ? _layout.cellSize.x : 0f,
                // the width the rows keep inside (the sheet's units), how many pictures that is to a row, and the rows that makes
                gridWidth = _gridWidth, perRow = _layout != null ? _layout.constraintCount : 0,
                rows = _layout != null && _layout.constraintCount > 0 ? (_cells.Count + _layout.constraintCount - 1) / _layout.constraintCount : 0,
                // the grid's own box and DD2's panel of the skills on DD1's screen, "x,y wxh"
                grid = _grid != null ? SheetEquipment.Box(_grid) : null, cells
            };
        }
    }

    /// <summary>One camping skill of the view: DD2's skill button, told what a pointer does to it.</summary>
    internal class SheetCampCell : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public SheetCampingView View;
        public string SkillId;
        public bool Ready;
        public GameObject Hover;
        /// <summary>DD1's picture of the skill in the button.</summary>
        public RectTransform Picture;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left && View != null) View.Press(SkillId);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (View != null) View.Hovered(this, true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (View != null) View.Hovered(this, false);
        }

        private void OnDisable()
        {
            if (Hover != null) Hover.SetActive(false);
        }
    }

    /// <summary>The switch's word is part of the switch: it lights up under the pointer as the diamond does, and a click on it switches.</summary>
    internal class SheetCampSwitchWord : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public SheetCampingView View;
        public TextMeshProUGUI Label;
        public Color Rest, Over;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left && View != null) View.Flip();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (Label != null) Label.color = Over;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (Label != null) Label.color = Rest;
        }

        private void OnDisable()
        {
            if (Label != null) Label.color = Rest;
        }
    }
}
