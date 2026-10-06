using System;
using System.Collections;
using System.Collections.Generic;
using Assets.Code.CommonLogic.Pooling;
using Assets.Code.CommonLogic.Presentation;
using Assets.Code.UI;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// What DD2's fight shows under an actor, for the heroes in a corridor and at a camp (the owner: "I do not
    /// like the look of the DD1 HUD panels under the heroes; make them as in a fight in DD2"): the health bar with its
    /// quarter notches, its wounded end and the bar that trails a change, the ten stress pips, the row of marks
    /// under them, and the selection mark.
    ///
    /// They are copies of the fight's own widgets. The bars are the template the fight's HUD spawns its actor
    /// panels from (BattleInfoUiBhv.m_actorInfoPrefab: <see cref="ActorInfoUiBhv"/>, a pooled panel that
    /// follows its actor's x and stands on a fixed line of the screen); the selection marks are the first of
    /// the fight's "performer" and "friendly" indicators (ActorIndicatorSpawnerBhv). Both come out of the
    /// prefab of the fight's HUD (<see cref="Dd2Hud"/>). A copy is stripped of the game's behaviours (they
    /// listen to the fight) and what those would have set up is set up here, the way the game's code does it:
    /// the bar's size for an actor of one rank, the notches, the pips (StressBarBhv.AddStressPip), everything
    /// the debug switch hides. Then <see cref="View"/> drives it: StatusBarBhv's sliders by a hero's health,
    /// the pips by their stress.
    ///
    /// The places are the fight's (read from the live HUD of a fight, Arena/CombatUI/BattleInfo/ActorInfos, on
    /// a 1080 px high screen): a panel's top stands 402.5 px above the screen's foot, its health bar's middle
    /// 32.5 px under that, the pips' 53, the marks' 77.5; the selection mark's foot 47.5.
    /// </summary>
    internal static class Dd2Bars
    {
        /// <summary>A panel's top (its pivot) above the screen's foot in a fight, and its parts under the top.</summary>
        public const float FightTopFromFoot = 402.5f;
        public const float BarMiddle = 32.5f, BarHeight = 13f, PipsMiddle = 53f, MarksMiddle = 77.5f, IndicatorFoot = 47.5f;
        public const int Pips = 10;
        /// <summary>
        /// The row of stress pips as a fight's scene has it (the scene overrides the HUD's prefab): the box the
        /// pips are set out in from its left edge, and where the panel that holds it stands, in the panel's pixels.
        /// </summary>
        public const float FightPipRowWidth = 175f, FightPipRowX = 0f, FightStressPanelX = -5f;

        private static GameObject _template, _selected, _target, _token;
        private static bool _failed, _indicatorsDone;
        private static int[] _main, _back, _backFill, _wounded, _woundNotch, _pipRow, _markRow, _hover, _deathsDoor, _recovery;
        private static int[] _tokenIcon, _tokenPlate;
        private static Color _damage = new Color32(0xd0, 0x69, 0x00, 0xff), _heal = new Color32(0x0d, 0x9d, 0x1f, 0xff);
        private static Color _pipLit = Color.white, _pipDark = new Color32(0x1e, 0x1e, 0x1e, 0xff);
        private static float _delay = 0.45f, _speed = 0.8f;
        private static Sprite _buff, _debuff;

        /// <summary>True once the panel is copied and the selection marks are settled (copied, or not to be had).</summary>
        public static bool Ready => _template != null && _indicatorsDone;
        public static string Status { get; private set; } = "not asked for";

        /// <summary>DD2's own pictures for "this actor has buffs" and "... debuffs" (beside a fight's health bar).</summary>
        public static Sprite BuffSprite => _buff;
        public static Sprite DebuffSprite => _debuff;

        /// <summary>Asks for the widgets; they are there some frames later. Called every frame while the Estate is up.</summary>
        public static void Prepare()
        {
            if (_failed || !Dd2Hud.BarsDd2) return;
            try
            {
                if (_template == null) PrepareBars();
                if (_template != null && !_indicatorsDone) PrepareIndicators();
            }
            catch (Exception e)
            {
                _failed = true;
                Status = "failed: " + e.Message;
                Plugin.Log.LogWarning("DD2 bars: the fight's actor panel could not be copied (" + e + "); DD1's bars stay");
                Drop();
            }
        }

        private static void Drop()
        {
            foreach (var old in new[] { _template, _selected, _target, _token })
                if (old != null) UnityEngine.Object.Destroy(old);
            _template = _selected = _target = _token = null;
            _indicatorsDone = false;
        }

        /// <summary>Dev bridge: the copies are thrown away and made again.</summary>
        public static void Reset()
        {
            Drop();
            _failed = false;
            Status = "asked for again";
        }

        private static T F<T>(object target, string name) where T : class
        {
            return target != null ? AccessTools.Field(target.GetType(), name)?.GetValue(target) as T : null;
        }

        private static object V(object target, string name)
        {
            return target != null ? AccessTools.Field(target.GetType(), name)?.GetValue(target) : null;
        }

        private static bool Same(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f && Mathf.Abs(a.b - b.b) < 0.01f;
        }

        private static void PrepareBars()
        {
            var hud = Dd2Hud.FightHud(out var waiting);
            GameObject source = null;
            if (hud != null)
            {
                var battle = hud.GetComponentInChildren<BattleInfoUiBhv>(true);
                source = F<GameObject>(battle, "m_actorInfoPrefab");
            }
            if (source == null)
            {
                if (waiting)
                {
                    Status = "loading";
                    return;
                }
                // FALLBACK: the actor panel's own prefab (without what the fight's HUD changes on it: the buff arrows' pictures, the fill's red)
                source = Dd2Hud.Prefab(Dd2Hud.ActorInfoKey);
                if (source == null)
                {
                    if (!Dd2Hud.Answered(Dd2Hud.ActorInfoKey))
                    {
                        Status = "loading the actor panel";
                        return;
                    }
                    throw new InvalidOperationException("the game hands over neither the fight's HUD nor the actor panel");
                }
            }
            Capture(source);
        }

        private static void Capture(GameObject source)
        {
            // Under a switched-off parent nothing of the copy wakes up until it is put on screen.
            var clone = UnityEngine.Object.Instantiate(source, Dd2Hud.Holder, false);
            try
            {
                clone.name = "Dd2Bars";
                clone.SetActive(true);
                var root = clone.transform;
                var info = clone.GetComponent<ActorInfoUiBhv>();
                if (info == null) throw new InvalidOperationException("the template is not an actor panel");
                int[] PathOf(Component part) => part != null ? Dd2Hud.PathTo(root, part.transform) : null;

                F<GameObject>(info, "m_LivingPanelGO")?.SetActive(true);

                // ---- the health bar: StatusBarBhv's sliders ----
                var health = F<StatusBarBhv>(info, "m_HealthBar");
                if (health == null) throw new InvalidOperationException("the panel has no health bar");
                health.gameObject.SetActive(true);
                var main = F<Slider>(health, "m_Slider");
                var back = F<Slider>(health, "m_BackSlider");
                var backFill = F<Image>(health, "m_BackSliderFill");
                var wounded = F<Slider>(health, "m_woundedSlider");
                var woundNotch = F<GameObject>(health, "m_woundedNotchObj");
                if (main == null || back == null) throw new InvalidOperationException("the health bar has no sliders");
                _main = PathOf(main);
                _back = PathOf(back);
                _backFill = PathOf(backFill);
                _wounded = PathOf(wounded);
                _woundNotch = woundNotch != null ? Dd2Hud.PathTo(root, woundNotch.transform) : null;
                if (V(health, "m_DamageColor") is Color damage) _damage = damage;
                if (V(health, "m_HealColor") is Color heal) _heal = heal;
                if (V(health, "m_ValueChangeDelay") is float delay) _delay = delay;
                if (V(health, "m_BarSpeed") is float speed && speed > 0f) _speed = speed;
                F<TextMeshProUGUI>(health, "m_healPreviewLabel")?.gameObject.SetActive(false);
                F<TextMeshProUGUI>(health, "m_toggleableHealthLabel")?.gameObject.SetActive(false);
                main.value = back.value = 1f;
                if (backFill != null) backFill.color = Color.clear;
                if (wounded != null) wounded.value = 0f;
                if (woundNotch != null) woundNotch.SetActive(false);

                // ActorInfoUiBhv.ResizeHealthBars, for an actor one rank wide
                var bar = F<RectTransform>(info, "m_HealthBarObj");
                if (bar != null && V(info, "m_healthBarSizes") is IList sizes && sizes.Count > 0 && sizes[0] is Vector2 size) bar.sizeDelta = size;
                var notches = V(info, "m_quarterHealthNotches") as RectTransform[];
                if (notches != null && notches.Length >= 3 && V(info, "m_quarterHealthNotchPositions") is Vector3[] places && places.Length > 0)
                {
                    var at = new[] { places[0].x, places[0].y, places[0].z };
                    for (var i = 0; i < 3; i++)
                        if (notches[i] != null) notches[i].anchoredPosition = new Vector2(at[i], notches[i].anchoredPosition.y);
                }
                F<RectTransform>(info, "m_healthNotch")?.gameObject.SetActive(false);

                // ---- the stress pips: StressBarBhv.AddStressPip ----
                var stress = F<StressBarBhv>(info, "m_StressBar");
                var pip = F<GameObject>(stress, "m_stressPipPrefab");
                var row = F<Transform>(stress, "m_stressPipContainer");
                if (stress == null || pip == null || row == null) throw new InvalidOperationException("the panel has no stress pips");
                F<GameObject>(stress, "m_root")?.SetActive(true);
                stress.gameObject.SetActive(true);
                if (V(stress, "m_stressPipActiveColor") is Color32 lit) _pipLit = lit;
                if (V(stress, "m_stressPipInactiveColor") is Color32 dark) _pipDark = dark;
                var sprites = V(stress, "m_stressPipSprites") as IList;
                var small = V(stress, "m_smallSize") is Vector2 s ? s : new Vector2(12f, 12f);
                var large = V(stress, "m_largeSize") is Vector2 g ? g : new Vector2(16f, 24f);
                var threshold = V(stress, "m_largePipThreshold") is int t ? t : 2;
                // What the fight's scene changes on the panel (read from the live HUD of a fight, whose own template
                // is Arena/CombatUI/BattleInfo/ActorInfos/actor_info_panel): put on whatever the copy was taken from.
                var fill = main.fillRect != null ? main.fillRect.GetComponent<Image>() : null;
                if (fill != null && Same(fill.color, new Color32(0x96, 0x0e, 0x00, 0xff))) fill.color = new Color32(0x83, 0x0a, 0x1b, 0xff);
                // The row of pips. Its layout sets the pips out from the row's left edge and fits nothing
                // (HorizontalLayoutGroup, MiddleLeft), so where the pips stand is the row's own box: in a fight's
                // scene 175 wide at 0, which puts the ten pips under the bar. The HUD's prefab, which the copy is
                // taken from, has the row 0 wide at 5: a row of no width begins at the panel's middle, and the
                // pips stood half a bar to the right of their health bar (the owner's screenshot of 2026-10-06).
                // The panel's own prefab has a third pair, 190 at 1.253. The fight's are put on all of them.
                var stressPanel = F<GameObject>(stress, "m_root");
                var stressRect = stressPanel != null ? stressPanel.transform as RectTransform : null;
                if (stressRect != null) stressRect.anchoredPosition = new Vector2(FightStressPanelX, stressRect.anchoredPosition.y);
                if (row is RectTransform rowRect)
                {
                    rowRect.sizeDelta = new Vector2(FightPipRowWidth, rowRect.sizeDelta.y);
                    rowRect.anchoredPosition = new Vector2(FightPipRowX, rowRect.anchoredPosition.y);
                }

                for (var i = row.childCount - 1; i >= 0; i--)
                    if (row.GetChild(i).gameObject != pip) UnityEngine.Object.DestroyImmediate(row.GetChild(i).gameObject);
                for (var i = 0; i < Pips; i++)
                {
                    var one = UnityEngine.Object.Instantiate(pip, row, false);
                    one.name = "Pip" + i;
                    ((RectTransform)one.transform).sizeDelta = i > threshold ? large : small;
                    var image = one.GetComponent<Image>();
                    if (image != null)
                    {
                        if (sprites != null && sprites.Count > 1) image.sprite = sprites[i % (sprites.Count - 1)] as Sprite;
                        image.color = _pipDark;
                    }
                    one.SetActive(true);
                }
                if (pip.transform.IsChildOf(root)) pip.SetActive(false);
                _pipRow = Dd2Hud.PathTo(root, row);

                // ---- the row of marks: the tokens' pool, with the game's own marks for death's door in it ----
                var pool = F<GameObjectPoolBhv>(info, "m_TokenPoolBhv");
                if (pool == null) throw new InvalidOperationException("the panel has no row of tokens");
                _markRow = PathOf(pool);
                GameObject token = pool.prefab;
                if (V(info, "m_BuffIcons") is IList icons)
                    foreach (var entry in icons)
                    {
                        var icon = entry as GameObject;
                        if (icon == null) continue;
                        icon.SetActive(false);
                        var picture = icon.GetComponent<Image>();
                        switch (icon.name)
                        {
                            case "deaths_door_icon": _deathsDoor = Dd2Hud.PathTo(root, icon.transform); break;
                            case "deaths_door_recovery_icon": _recovery = Dd2Hud.PathTo(root, icon.transform); break;
                            case "buff_icon": _buff = picture != null ? picture.sprite : null; break;
                            case "debuff_icon": _debuff = picture != null ? picture.sprite : null; break;
                        }
                    }

                // ---- what a fight's code keeps switched off until it is wanted ----
                foreach (var each in clone.GetComponentsInChildren<GameObjectPoolBhv>(true))
                    if (each.prefab != null && each.prefab.transform.IsChildOf(root)) each.prefab.SetActive(false);
                F<GameObject>(info, "m_hoverInfoPanel")?.SetActive(false);
                var highlight = F<CanvasGroup>(info, "m_modelHighlightGroup");
                if (highlight != null) highlight.alpha = 0f;
                // the fight's own click area over the actor: the dungeon's screen has its own
                F<CombatActorHitboxBhv>(info, "m_characterHitboxBhv")?.gameObject.SetActive(false);
                F<AffinityChangeUIBhv>(info, "m_affinityChangeBhv")?.gameObject.SetActive(false);
                // DD2's own area for the bars' tooltip (over the health bar and the pips)
                var tips = V(info, "m_statusBarTooltips") as IList;
                var tip = tips != null && tips.Count > 0 ? tips[0] as Component : null;
                _hover = tip != null && tip.GetComponent<Graphic>() != null ? PathOf(tip) : null;

                // ---- a token, for the marks DD2 has no picture of its own for ----
                if (token != null)
                {
                    var part = token.GetComponent<TokenIconBhv>();
                    if (V(part, "m_stackPipGameObjects") is IList stack)
                        foreach (var entry in stack) (entry as GameObject)?.SetActive(false);
                    F<GameObject>(part, "m_rankTokenGlow")?.SetActive(false);
                    var icon = F<RectTransform>(part, "m_IconTransform");
                    _token = UnityEngine.Object.Instantiate(token, Dd2Hud.Holder, false);
                    _token.name = "Dd2Mark";
                    _token.SetActive(true);
                    _tokenIcon = icon != null ? Dd2Hud.PathTo(token.transform, icon) : null;
                    var plate = token.transform.Find("BG");
                    _tokenPlate = plate != null ? Dd2Hud.PathTo(token.transform, plate) : null;
                    // the token's own sparkle hangs in a canvas of its own
                    foreach (var canvas in _token.GetComponentsInChildren<Canvas>(true)) canvas.gameObject.SetActive(false);
                    Dd2Hud.Strip(_token, false);
                    foreach (var group in _token.GetComponentsInChildren<CanvasGroup>(true))
                        if (group.gameObject == _token || (plate != null && group.transform.name == "BG") || (icon != null && group.transform.name == icon.name)) group.alpha = 1f;
                }

                foreach (var canvas in clone.GetComponentsInChildren<Canvas>(true)) canvas.gameObject.SetActive(false);
                var removed = Dd2Hud.Strip(clone, false);
                _template = clone;
                Status = "ready";
                Plugin.Log.LogInfo("DD2 bars: the fight's actor panel is copied (behaviours left out: " + string.Join(", ", removed) + ")");
            }
            catch (Exception)
            {
                UnityEngine.Object.Destroy(clone);
                throw;
            }
        }

        // The selection marks: the fight's indicator of whose turn it is (gold), and of a companion who may be chosen (green).
        private static void PrepareIndicators()
        {
            var hud = Dd2Hud.FightHud(out var waiting);
            var spawner = hud != null ? hud.GetComponentInChildren<ActorIndicatorSpawnerBhv>(true) : null;
            if (spawner == null)
            {
                if (waiting) return;
                var own = Dd2Hud.Prefab(Dd2Hud.IndicatorsKey);
                if (own == null && !Dd2Hud.Answered(Dd2Hud.IndicatorsKey)) return;
                spawner = own != null ? own.GetComponentInChildren<ActorIndicatorSpawnerBhv>(true) : null;
            }
            _indicatorsDone = true;
            try
            {
                var marks = F<GameObject>(spawner, "m_ActorSelectionIndicatorPrefab");
                var indicator = marks != null ? marks.GetComponent<ActorSelectionIndicatorBhv>() : null;
                if (indicator == null)
                {
                    Plugin.Log.LogWarning("DD2 bars: the fight's selection marks are not to be had; DD1's bracket marks the selected hero");
                    return;
                }
                _selected = Indicator(V(indicator, "m_performerIndicators") as GameObject[], "Dd2Selected");
                _target = Indicator(V(indicator, "m_friendlyIndicators") as GameObject[], "Dd2Target");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("DD2 bars: the fight's selection marks could not be copied (" + e.Message + "); DD1's bracket marks the selected hero");
                _selected = _target = null;
            }
        }

        private static GameObject Indicator(GameObject[] bySize, string name)
        {
            if (bySize == null || bySize.Length == 0 || bySize[0] == null) return null;
            var clone = UnityEngine.Object.Instantiate(bySize[0], Dd2Hud.Holder, false);
            clone.name = name;
            clone.SetActive(true);
            // at rest, as the fight shows it while nothing is aimed at the actor: without the game's animator
            Dd2Hud.Strip(clone, false);
            return clone;
        }

        /// <summary>
        /// A panel on screen under <paramref name="parent"/>, its top middle at the parent's origin (the caller
        /// puts it where it belongs); null while the copy is not there.
        /// </summary>
        public static View Create(Transform parent)
        {
            if (!Ready) return null;
            try
            {
                var root = (RectTransform)UnityEngine.Object.Instantiate(_template, parent, false).transform;
                root.name = "Dd2Bars";
                root.anchorMin = root.anchorMax = new Vector2(0f, 1f);
                root.pivot = new Vector2(0.5f, 1f);
                root.anchoredPosition = Vector2.zero;
                root.localRotation = Quaternion.identity;
                root.localScale = Vector3.one * Dd2Hud.Scale;
                root.gameObject.SetActive(true);
                return new View(root);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("DD2 bars: a panel could not be put on screen (" + e.Message + "); DD1's bars stay");
                _failed = true;
                Drop();
                return null;
            }
        }

        /// <summary>A mark in a panel's row.</summary>
        internal class Mark
        {
            public RectTransform Rect;
            /// <summary>What takes the pointer for it.</summary>
            public Graphic Graphic;
            /// <summary>True for a mark of the game's own that is only switched on and off.</summary>
            public bool Own;
            public Image Picture;
        }

        /// <summary>One panel on screen.</summary>
        internal class View
        {
            public readonly RectTransform Root;
            private readonly Slider _mainBar, _backBar, _woundBar;
            private readonly Image _backImage;
            private readonly GameObject _notch;
            private readonly List<Image> _pips = new List<Image>();
            private readonly Transform _row, _pipsRow;
            private readonly Mark _door, _mend;
            private GameObject _selectedMark, _targetMark;
            private bool _updating, _healing, _set;
            private float _since;

            /// <summary>DD2's own area over the health bar and the pips, for the pointer; null when the panel has none.</summary>
            public readonly Graphic BarsArea;

            public View(RectTransform root)
            {
                Root = root;
                _mainBar = Dd2Hud.At<Slider>(root, _main);
                _backBar = Dd2Hud.At<Slider>(root, _back);
                _backImage = Dd2Hud.At<Image>(root, _backFill);
                _woundBar = Dd2Hud.At<Slider>(root, _wounded);
                var notch = Dd2Hud.At(root, _woundNotch);
                _notch = notch != null ? notch.gameObject : null;
                var pips = _pipsRow = Dd2Hud.At(root, _pipRow);
                if (pips != null)
                    for (var i = 0; i < pips.childCount; i++)
                    {
                        var pip = pips.GetChild(i);
                        var image = pip.GetComponent<Image>();
                        if (pip.gameObject.activeSelf && image != null) _pips.Add(image);
                    }
                _row = Dd2Hud.At(root, _markRow);
                _door = Own(_deathsDoor);
                _mend = Own(_recovery);
                BarsArea = Dd2Hud.At<Graphic>(root, _hover);
            }

            private Mark Own(int[] path)
            {
                var part = Dd2Hud.At(Root, path);
                var image = part != null ? part.GetComponent<Image>() : null;
                return image != null ? new Mark { Rect = (RectTransform)part, Graphic = image, Picture = image, Own = true } : null;
            }

            public bool HasSelectionMarks => Dd2Bars._selected != null;

            // Dev bridge (hud.bars): the parts whose places are measured against a fight's.
            public Transform BarPart => _mainBar != null ? _mainBar.transform : null;
            public Transform PipRowPart => _pipsRow;
            public Transform MarkRowPart => _row;
            public Transform SelectedPart => _selectedMark != null && _selectedMark.activeSelf ? _selectedMark.transform : null;
            public Transform TargetPart => _targetMark != null && _targetMark.activeSelf ? _targetMark.transform : null;

            /// <summary>DD2's own mark for death's door, and for what it leaves behind; null when the panel has none.</summary>
            public Mark DeathsDoor => _door;
            public Mark Recovery => _mend;

            /// <summary>
            /// A mark of the mod's in the row: DD2's token with another picture on it. <paramref name="size"/>
            /// is the picture's size in the token's 32 px (zero: the whole token, as DD2's own token pictures);
            /// <paramref name="plate"/> the token's black plate under it.
            /// </summary>
            public Mark AddMark(Sprite sprite, Vector2 size, bool plate)
            {
                if (_row == null || Dd2Bars._token == null) return null;
                var token = (RectTransform)UnityEngine.Object.Instantiate(Dd2Bars._token, _row, false).transform;
                token.name = "Mark";
                var icon = Dd2Hud.At(token, _tokenIcon) as RectTransform;
                var picture = icon != null ? icon.GetComponent<Image>() : null;
                if (picture == null)
                {
                    UnityEngine.Object.Destroy(token.gameObject);
                    return null;
                }
                picture.overrideSprite = null;
                picture.sprite = sprite;
                picture.enabled = sprite != null;
                if (size.x > 0f && size.y > 0f)
                {
                    icon.anchorMin = icon.anchorMax = icon.pivot = new Vector2(0.5f, 0.5f);
                    icon.anchoredPosition = Vector2.zero;
                    icon.sizeDelta = size;
                }
                var ground = Dd2Hud.At(token, _tokenPlate);
                var groundImage = ground != null ? ground.GetComponent<Image>() : null;
                if (ground != null && !plate && groundImage != null)
                {
                    // the plate stays for the pointer, unseen
                    foreach (var graphic in ground.GetComponentsInChildren<Graphic>(true)) graphic.color = Color.clear;
                }
                token.gameObject.SetActive(true);
                return new Mark { Rect = token, Graphic = groundImage != null ? (Graphic)groundImage : picture, Picture = picture };
            }

            public void RemoveMark(Mark mark)
            {
                if (mark == null || mark.Rect == null) return;
                mark.Rect.localScale = Vector3.one;
                mark.Rect.gameObject.SetActive(false);
                if (!mark.Own) UnityEngine.Object.Destroy(mark.Rect.gameObject);
            }

            /// <summary>The marks stand in the row in the order given (the game's own first, as it has them).</summary>
            public void Order(IReadOnlyList<Mark> marks)
            {
                if (_row == null) return;
                var at = _row.childCount;
                for (var i = marks.Count - 1; i >= 0; i--)
                    if (marks[i] != null && marks[i].Rect != null && marks[i].Rect.parent == _row) marks[i].Rect.SetSiblingIndex(--at);
            }

            /// <summary>
            /// Health as StatusBarBhv shows it: <paramref name="share"/> of the bar is filled, the last
            /// <paramref name="wound"/> of it is hatched as wounded. A change trails behind (the old amount
            /// stays for a moment in DD2's colour for a loss, or the new one comes in its colour for a gain)
            /// unless <paramref name="animate"/> is false.
            /// </summary>
            public void SetHealth(float share, float wound, bool animate)
            {
                if (_mainBar == null || _backBar == null) return;
                share = Mathf.Clamp01(share);
                wound = Mathf.Clamp01(wound);
                if (_woundBar != null) _woundBar.value = wound;
                if (_notch != null && _notch.activeSelf != wound > 0f) _notch.SetActive(wound > 0f);
                if (!animate || !_set)
                {
                    _set = true;
                    _mainBar.value = _backBar.value = share;
                    if (_backImage != null) _backImage.color = Color.clear;
                    _updating = false;
                    return;
                }
                // StatusBarBhv.SetValue
                if (!_updating)
                {
                    _since = 0f;
                    var shown = _mainBar.value;
                    if (Mathf.Abs(share - shown) > 0.0005f) _updating = true;
                    _healing = share > shown && _updating;
                    if (!_updating) return;
                    if (_healing)
                    {
                        if (_backImage != null) _backImage.color = _heal;
                        _backBar.value = share;
                    }
                    else
                    {
                        if (_backImage != null) _backImage.color = _damage;
                        _mainBar.value = share;
                    }
                }
                else if (_healing) _backBar.value = Mathf.Max(share, _mainBar.value);
                else _mainBar.value = Mathf.Min(share, _backBar.value);
            }

            /// <summary>Every frame: StatusBarBhv.UpdateAnimation.</summary>
            public void Tick(float dt)
            {
                if (!_updating || _mainBar == null || _backBar == null) return;
                if (_mainBar.value < _backBar.value - 0.0005f)
                {
                    _since += dt;
                    if (_since < _delay) return;
                    if (_healing) _mainBar.value = Mathf.Min(_backBar.value, _mainBar.value + _speed * dt);
                    else _backBar.value = Mathf.Max(_mainBar.value, _backBar.value - _speed * dt);
                    return;
                }
                _backBar.value = _mainBar.value;
                if (_backImage != null) _backImage.color = Color.clear;
                _updating = false;
            }

            /// <summary>StressBarBhv.SetValue: the first <paramref name="lit"/> pips are lit.</summary>
            public void SetStress(int lit)
            {
                for (var i = 0; i < _pips.Count; i++)
                {
                    var colour = i < lit ? _pipLit : _pipDark;
                    if (_pips[i].color != colour) _pips[i].color = colour;
                }
            }

            public bool Selected
            {
                set => Show(ref _selectedMark, Dd2Bars._selected, value);
            }

            public bool Target
            {
                set => Show(ref _targetMark, Dd2Bars._target, value);
            }

            // A fight's indicator stands behind the actors' panels, its foot on its own line.
            private void Show(ref GameObject mark, GameObject template, bool on)
            {
                if (mark == null)
                {
                    if (!on || template == null) return;
                    var rect = (RectTransform)UnityEngine.Object.Instantiate(template, Root, false).transform;
                    rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
                    rect.pivot = new Vector2(0.5f, 0f);
                    rect.anchoredPosition = new Vector2(0f, -IndicatorFoot);
                    rect.localScale = Vector3.one;
                    rect.SetAsFirstSibling();
                    mark = rect.gameObject;
                }
                if (mark.activeSelf != on) mark.SetActive(on);
            }

            /// <summary>Dev bridge: what the panel shows.</summary>
            public object Describe()
            {
                var lit = 0;
                foreach (var pip in _pips)
                    if (pip.color == _pipLit) lit++;
                return new
                {
                    health = _mainBar != null ? _mainBar.value : -1f,
                    trail = _backBar != null ? _backBar.value : -1f,
                    wounded = _woundBar != null ? _woundBar.value : -1f,
                    pips = _pips.Count, lit,
                    selected = _selectedMark != null && _selectedMark.activeSelf,
                    target = _targetMark != null && _targetMark.activeSelf,
                    scale = Root.localScale.x
                };
            }
        }
    }
}
