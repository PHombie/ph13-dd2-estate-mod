using System;
using Assets.Code.Game.StageCoach;
using Assets.Code.UI;
using Assets.Code.UI.DataContext;
using Assets.Code.UI.Managers;
using Assets.Code.Utils;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// DD2's own torch for the dungeon view (the owner's choice over DD1's overlays/torch.png, final since
    /// 2026-10-06): the widget of DD2's fights, a torch under its crown with a flame the game's shader animates
    /// and a bar burning out to both sides.
    ///
    /// The widget (<see cref="CombatTorchUiBhv"/>) lives in the fight's scene, which is not loaded in the hamlet or a
    /// corridor. It is taken from the prefab of the fight's HUD (<see cref="Dd2Hud"/>: the very widget of a
    /// fight, with the fight's sizes), and when that is not to be had from the game's story screen, which
    /// carries another copy of it (CommonUiBhv.m_storyPrefab: there the torch's stick is drawn longer, and the
    /// fight's measures are put on it by hand). The copy is stripped of the game's behaviours (they read the
    /// run's torch, the coach's flame item, the fight's presentation) and driven by the expedition's light
    /// instead: the two sliders, and the flame's and the bar's material by DD2's own thresholds. What the widget
    /// takes from the torch in the coach's flame slot (the bar's burning ends, the embers, the glow's colour) is
    /// taken from the game's default torch.
    ///
    /// The bars are DD2's own, bare: two lines of fire with nothing around them, in a fight and in the copy alike
    /// (the owner, 2026-10-06, of the frame the first batch put round them: "remove the lattice, we stay without it").
    /// </summary>
    internal static class Dd2Torch
    {
        /// <summary>
        /// On: DD2's widget is the torch of the dungeon's screen. Off ([Look] Torch = dd1, or the dev bridge):
        /// DD1's own torch (overlays/torch.png and its flame, <see cref="RaidTorch"/>).
        /// </summary>
        public static bool Enabled = true;
        /// <summary>Where a copy taken from the story screen hangs from the top middle of the screen (DD2's fights: 0, -42), and a factor on any copy's size.</summary>
        public static Vector2 Position = new Vector2(0f, -42f);
        public static float Scale = 1f;
        /// <summary>Seconds one look of the flame and its bars takes to run into the next as the gauge passes a threshold; 0: from one frame to the next, as DD2 does it.</summary>
        public static float LevelTime = 0.35f;

        /// <summary>Dev bridge: DD2's four looks of the flame and of the bar, each material with its numbers.</summary>
        public static object DescribeLevels()
        {
            var flames = new System.Collections.Generic.List<object>();
            var bars = new System.Collections.Generic.List<object>();
            for (var i = 0; i < FlameLevels.Length; i++)
            {
                flames.Add(Dev.PassageFilm.MaterialNumbers(FlameLevels[i]));
                bars.Add(Dev.PassageFilm.MaterialNumbers(BarLevels[i]));
            }
            return new { thresholds = new[] { _low, _medium, _high }, levelTime = LevelTime, flames, bars };
        }

        // The fight's own measures (read from the live widget of a fight, Arena/CombatUI/BattleInfo/Torch), for a copy from the story screen.
        private static readonly Vector2 FightRoot = new Vector2(72f, 45f);
        private static readonly Vector2 FightStick = new Vector2(90f, 227f);       // the stick's box; its picture keeps its own proportions in it

        private static bool _storyAsked, _failed;
        private static AsyncOperationHandle<GameObject> _story;
        private static GameObject _template;
        private static bool _fromFight;
        private static Vector2 _hang;
        private static int[] _right, _left, _flame, _fillRight, _fillLeft, _director, _sparksUp, _sparksDown;
        private static UnityEngine.Playables.PlayableAsset _increase, _decrease;
        /// <summary>
        /// The widget answers a torch lit or put down as DD2's own does in a fight: its "increase" (or "decrease")
        /// timeline is played (a flare of the glow behind the crown) with the burst of embers the coach's torch
        /// gives it. Off: only the bars and the flame's look move.
        /// </summary>
        public static bool Flares = true;
        private static readonly Material[] FlameLevels = new Material[4];
        private static readonly Material[] BarLevels = new Material[4];
        private static float _low = 0.25f, _medium = 0.5f, _high = 0.75f;

        public static bool Ready => _template != null;
        public static string Status { get; private set; } = "not asked for";
        /// <summary>Dev bridge: true when the copy is of the fight's own widget (and not of the story screen's).</summary>
        public static bool FromFight => _fromFight;
        /// <summary>Counts the copies made: a widget on screen that was made from an older one is made again.</summary>
        public static int Made { get; private set; }

        /// <summary>Asks for the widget; it is there some frames later. Called every frame while the Estate is up.</summary>
        public static void Prepare()
        {
            // the heroes' bars and the banner's skills come out of the same prefab, whichever torch is shown
            Dd2Bars.Prepare();
            Dd2Skills.Prepare();
            if (_template != null || _failed || !Enabled) return;
            try
            {
                var hud = Dd2Hud.FightHud(out var waiting);
                if (hud != null)
                {
                    var own = hud.GetComponentInChildren<CombatTorchUiBhv>(true);
                    if (own != null)
                    {
                        Capture(own, true);
                        return;
                    }
                }
                if (waiting)
                {
                    Status = "loading";
                    return;
                }
                // FALLBACK: the story screen's copy of the widget
                if (_storyAsked) return;
                _storyAsked = true;
                var common = SingletonMonoBehaviour<CommonUiBhv>.Instance;
                var reference = common != null ? AccessTools.Field(typeof(CommonUiBhv), "m_storyPrefab")?.GetValue(common) as AssetReference : null;
                if (reference == null || reference.RuntimeKey == null)
                {
                    Fail("neither the fight's HUD nor the story screen has an address here");
                    return;
                }
                Status = "loading the story screen";
                // a handle of the mod's own: the game loads and releases its own as stories come and go
                _story = Addressables.LoadAssetAsync<GameObject>(reference.RuntimeKey);
                _story.Completed += loaded =>
                {
                    try
                    {
                        var source = loaded.Status == AsyncOperationStatus.Succeeded && loaded.Result != null ? loaded.Result.GetComponentInChildren<CombatTorchUiBhv>(true) : null;
                        if (source == null) Fail("the story screen has no torch in it");
                        else Capture(source, false);
                    }
                    catch (Exception e) { Fail("the widget could not be copied (" + e.Message + ")"); }
                };
            }
            catch (Exception e) { Fail(e.Message); }
        }

        private static void Fail(string why)
        {
            _failed = true;
            Status = "failed: " + why;
            Plugin.Log.LogWarning("DD2 torch: " + why + "; DD1's torch is shown");
            if (_template != null) UnityEngine.Object.Destroy(_template);
            _template = null;
        }

        /// <summary>Dev bridge: the copy is thrown away and made again (from another source).</summary>
        public static void Reset()
        {
            if (_template != null) UnityEngine.Object.Destroy(_template);
            _template = null;
            _failed = false;
            _storyAsked = false;
            Status = "asked for again";
        }

        private static void Capture(CombatTorchUiBhv source, bool fromFight)
        {
            // Under a switched-off parent nothing of the copy wakes up until it is put on screen.
            var clone = UnityEngine.Object.Instantiate(source.gameObject, Dd2Hud.Holder, false);
            try
            {
                clone.name = "Dd2Torch";
                var widget = clone.GetComponent<CombatTorchUiBhv>();

                T Field<T>(string name) where T : class => AccessTools.Field(typeof(CombatTorchUiBhv), name)?.GetValue(widget) as T;
                int[] PathOf(Component part) => part != null ? Dd2Hud.PathTo(clone.transform, part.transform) : null;

                var right = Field<Slider>("m_barSlider");
                var left = Field<Slider>("m_barSliderLeft");
                // The crown (the spiked ring around the torch) is switched off in the HUD's prefab and on in every
                // fight seen (the fight's own code brings it in as the bar first fills): on, as a fight shows it.
                var crown = clone.transform.Find("Crown");
                if (crown != null) crown.gameObject.SetActive(true);
                // In a fight's scene the right bar is turned over (its scale 1, -1, 1; the left one is turned round by
                // half a turn), so that the two are mirror images; the prefab has it upright.
                if (right != null && right.transform.localScale == Vector3.one) right.transform.localScale = new Vector3(1f, -1f, 1f);
                _right = PathOf(right);
                _left = PathOf(left);
                _flame = PathOf(Field<Image>("m_torchFlame"));
                _fillRight = PathOf(Field<Image>("m_barFill"));
                _fillLeft = PathOf(Field<Image>("m_barFillLeft"));
                // what DD2's widget plays when its value changes (CombatTorchUiBhv.LateUpdate)
                _director = PathOf(Field<UnityEngine.Playables.PlayableDirector>("m_playableDirector"));
                _increase = Field<UnityEngine.Playables.PlayableAsset>("m_increaseTimeline");
                _decrease = Field<UnityEngine.Playables.PlayableAsset>("m_decreaseTimeline");
                _sparksUp = _sparksDown = null;
                if (_right == null || _left == null) throw new InvalidOperationException("the widget has no bars");
                var levels = new[] { "NoLight", "Low", "Med", "High" };
                for (var i = 0; i < levels.Length; i++)
                {
                    FlameLevels[i] = Field<Material>("m_torchMaterial" + levels[i]);
                    BarLevels[i] = Field<Material>("m_barMaterial" + levels[i]);
                }
                _low = Threshold(widget, "m_LowThreshold", _low);
                _medium = Threshold(widget, "m_MedThreshold", _medium);
                _high = Threshold(widget, "m_HighThreshold", _high);

                // What the widget takes from the torch the coach carries: the game's default torch here.
                var torch = Singleton<ResourceDatabaseTorch>.Instance != null ? Singleton<ResourceDatabaseTorch>.Instance.GetDefaultTorch() : null;
                if (torch != null)
                {
                    Burn(Field<Image>("m_barLeftBurnImage"), torch.m_combatBarLeftMaterial);
                    Burn(Field<Image>("m_barRightBurnImage"), torch.m_combatBarRightMaterial);
                    Burn(Field<Image>("m_barMiddleBurnImage"), torch.m_combatBarMiddleMaterial);
                    // the bursts of embers at a change (CombatTorchUiBhv.UpdateBurnMaterials hangs them here)
                    var bursts = Field<Transform>("m_torchChangeVfxParticleParent");
                    if (bursts != null)
                    {
                        if (torch.m_combatIncreaseVfxParticles != null) _sparksUp = PathOf(UnityEngine.Object.Instantiate(torch.m_combatIncreaseVfxParticles, bursts).transform);
                        if (torch.m_combatDecreaseVfxParticles != null) _sparksDown = PathOf(UnityEngine.Object.Instantiate(torch.m_combatDecreaseVfxParticles, bursts).transform);
                    }
                    var embers = Field<Transform>("m_vfxParticleParent");
                    if (embers != null && torch.m_combatParticleSystemLoop != null && embers.childCount == 0)
                        UnityEngine.Object.Instantiate(torch.m_combatParticleSystemLoop, embers);
                    // the glow behind the flame is coloured through the game's data binding
                    foreach (var tinted in clone.GetComponentsInChildren<UiDisplayColourBhv>(true))
                    {
                        var image = tinted.GetComponent<Image>();
                        if (image != null) image.color = torch.m_combatGlowColour;
                    }
                }

                // DD2's tooltip and its number are the fight's; the dungeon view has DD1's words in a tooltip of its own.
                var tooltip = Field<GameObject>("m_tooltipGO");
                if (tooltip != null) UnityEngine.Object.DestroyImmediate(tooltip);
                var number = Field<CanvasGroup>("m_torchValueAlpha");
                if (number != null) number.gameObject.SetActive(false);

                var removed = Dd2Hud.Strip(clone, true);
                var group = clone.GetComponent<CanvasGroup>();
                if (group != null) group.alpha = 1f;

                var root = (RectTransform)clone.transform;
                if (!fromFight)
                {
                    // the story screen draws the same widget with a longer stick: the fight's measures by hand
                    root.sizeDelta = FightRoot;
                    foreach (var image in clone.GetComponentsInChildren<Image>(true))
                    {
                        if (image.sprite == null || image.sprite.name != "ui_torchflame") continue;
                        image.preserveAspect = true;
                        ((RectTransform)image.transform).sizeDelta = FightStick;
                    }
                }
                _hang = fromFight ? root.anchoredPosition : Position;

                _fromFight = fromFight;
                _template = clone;
                Made++;
                Status = fromFight ? "ready (the fight's own widget)" : "ready (the story screen's widget)";
                Plugin.Log.LogInfo("DD2 torch: the widget is copied from " + (fromFight ? "the fight's HUD" : "the story screen") + " (behaviours left out: " + string.Join(", ", removed) + ")");
            }
            catch (Exception)
            {
                UnityEngine.Object.Destroy(clone);
                throw;
            }
        }

        private static void Burn(Image image, Material material)
        {
            if (image != null && material != null) image.material = material;
        }

        private static float Threshold(CombatTorchUiBhv widget, string field, float fallback)
        {
            try
            {
                var value = AccessTools.Field(typeof(CombatTorchUiBhv), field)?.GetValue(widget);
                var direct = value != null ? AccessTools.Field(value.GetType(), "m_directValue")?.GetValue(value) : null;
                return direct is float number && number > 0f && number < 1f ? number : fallback;
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        /// <summary>
        /// The widget on screen, hung from the top middle of <paramref name="parent"/> where a fight has it; null
        /// while it is not there yet. <paramref name="parent"/> is the dungeon's 1920x1080 screen, whose top is
        /// the display's.
        /// </summary>
        public static View Create(RectTransform parent)
        {
            if (!Ready || !Enabled) return null;
            try
            {
                var root = (RectTransform)UnityEngine.Object.Instantiate(_template, parent, false).transform;
                root.name = "Dd2Torch";
                root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 1f);
                var size = Dd2Hud.Scale;
                root.anchoredPosition = _hang * size;
                root.localScale = Vector3.one * (size * Scale);
                root.localRotation = Quaternion.identity;
                root.gameObject.SetActive(true);
                return new View(root);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("DD2 torch: the widget could not be put on screen (" + e.Message + "); DD1's torch stays");
                Enabled = false;
                return null;
            }
        }

        /// <summary>One widget on screen.</summary>
        internal class View
        {
            public readonly RectTransform Root;
            private readonly Slider _rightBar, _leftBar;
            private readonly Image _flameImage, _rightFill, _leftFill;
            private int _level = -1, _from = -1;
            private float _shown = -1f, _mix = 1f;
            private Material _flameOwn, _fillOwn;       // the widget's own, for the way from one look to the next
            private readonly UnityEngine.Playables.PlayableDirector _timeline, _burstUp, _burstDown;
            /// <summary>How often the widget flared (dev bridge).</summary>
            public int Flared;
            /// <summary>How often the flame's look changed (dev bridge: once a threshold, never back and forth).</summary>
            public int Changes;

            public View(RectTransform root)
            {
                Root = root;
                _rightBar = Dd2Hud.At<Slider>(root, _right);
                _leftBar = Dd2Hud.At<Slider>(root, _left);
                _flameImage = Dd2Hud.At<Image>(root, _flame);
                _rightFill = Dd2Hud.At<Image>(root, _fillRight);
                _leftFill = Dd2Hud.At<Image>(root, _fillLeft);
                _timeline = _director != null ? Dd2Hud.At<UnityEngine.Playables.PlayableDirector>(root, _director) : null;
                _burstUp = _sparksUp != null ? Dd2Hud.At<UnityEngine.Playables.PlayableDirector>(root, _sparksUp) : null;
                _burstDown = _sparksDown != null ? Dd2Hud.At<UnityEngine.Playables.PlayableDirector>(root, _sparksDown) : null;
            }

            /// <summary>
            /// Where the widget's parts are on the dungeon's 1920x1080 screen (y down from its top): the crown
            /// with the torch under it, and the bar at its full length. What the pointer is told on the torch
            /// is told over these.
            /// </summary>
            public Rect CrownArea
            {
                get
                {
                    var size = Root.localScale.x;
                    var centre = Centre;
                    // the crown is 200 wide; the torch's stick ends 178 under the widget's middle
                    return new Rect(centre.x - 100f * size, centre.y - 30f * size, 200f * size, 205f * size);
                }
            }

            public Rect BarArea
            {
                get
                {
                    var size = Root.localScale.x;
                    var centre = Centre;
                    // the bars: 500 to either side, 7 high, 76.5 under the widget's middle; a hand's breadth around them takes the pointer
                    return new Rect(centre.x - 500f * size, centre.y + (76.5f - 12f) * size, 1000f * size, 24f * size);
                }
            }

            // The middle of the widget's own box: its parts hang from there.
            private Vector2 Centre => new Vector2(960f + Root.anchoredPosition.x, -Root.anchoredPosition.y + Root.rect.height * 0.5f * Root.localScale.y);

            /// <summary>
            /// What the gauge shows of the light, 0..1: it comes here already on its way (<see cref="ShownLight"/>
            /// slides it at the pace of DD2's own widget), so the bars are simply put there. DD2's flame and its
            /// bars have four looks (CombatTorchUiBhv.UpdateBarMaterials: a material each above the high, the
            /// medium and the low threshold, and one below), and DD2 changes them from one frame to the next
            /// when the VALUE is set, before its bar has moved. Here the look changes when the bar itself passes
            /// the threshold, once on the way (it never turns back before it has arrived), and one look runs
            /// into the next over <see cref="LevelTime"/> instead of jumping.
            /// </summary>
            public void Set(float share)
            {
                if (Root == null) return;
                share = Mathf.Clamp01(share);
                _shown = share;
                if (_rightBar != null) _rightBar.value = share;
                if (_leftBar != null) _leftBar.value = share;
                var level = Core.LightEase.FlameLevel(share, _low, _medium, _high);
                if (_level < 0 || LevelTime <= 0f)
                {
                    // the first look, or no way asked for: as DD2 puts it on
                    if (level != _level) Put(level);
                    return;
                }
                if (level != _level)
                {
                    // (a way still under way is cut short: its goal is where the next one starts)
                    _from = _level;
                    _level = level;
                    _mix = 0f;
                    Changes++;
                }
                if (_mix >= 1f) return;
                _mix = Mathf.Min(1f, _mix + Time.unscaledDeltaTime / LevelTime);
                if (_mix >= 1f) Put(_level);
                else Mix(Mathf.SmoothStep(0f, 1f, _mix));
            }

            // One of DD2's four looks, as its own widget wears it.
            private void Put(int level)
            {
                _level = level;
                _mix = 1f;
                if (_flameImage != null && FlameLevels[level] != null) _flameImage.material = FlameLevels[level];
                if (_rightFill != null && BarLevels[level] != null) _rightFill.material = BarLevels[level];
                if (_leftFill != null && BarLevels[level] != null) _leftFill.material = BarLevels[level];
            }

            // Between two looks: every number and colour of the one material on its way to the other's. (DD2's
            // four flames are one shader, vfx/ui/torch_flame_new, with the same pictures: they differ in two
            // colours, eight numbers and the tiling and speed of their two noise pictures; the four bars,
            // vfx/ui/bar_fill, in two colours.)
            private void Mix(float t)
            {
                Blend(ref _flameOwn, _flameImage, FlameLevels[_from], FlameLevels[_level], t);
                Blend(ref _fillOwn, _rightFill, BarLevels[_from], BarLevels[_level], t);
                // the two bars are one look
                if (_leftFill != null && _rightFill != null && _leftFill.material != _rightFill.material) _leftFill.material = _rightFill.material;
            }

            private static void Blend(ref Material own, Image image, Material from, Material to, float t)
            {
                if (image == null || to == null) return;
                if (from == null || from.shader != to.shader)
                {
                    // two looks that are not the same kind of thing cannot run into each other
                    if (image.material != to) image.material = to;
                    return;
                }
                if (own == null) own = new Material(to) { name = to.name + " (on its way)", hideFlags = HideFlags.HideAndDontSave };
                // Everything of the nearer look first, then every number and colour between the two. Not the
                // vectors (Material.Lerp would take them too): they are the flame's tilings and panning speeds,
                // and a speed on its way from one value to another moves the picture by the game's whole clock
                // times the difference: the flame would race for the length of the change.
                own.CopyPropertiesFromMaterial(t < 0.5f ? from : to);
                var shader = to.shader;
                for (var i = 0; i < shader.GetPropertyCount(); i++)
                {
                    var id = shader.GetPropertyNameId(i);
                    switch (shader.GetPropertyType(i))
                    {
                        case UnityEngine.Rendering.ShaderPropertyType.Color:
                            own.SetColor(id, Color.LerpUnclamped(from.GetColor(id), to.GetColor(id), t));
                            break;
                        case UnityEngine.Rendering.ShaderPropertyType.Float:
                        case UnityEngine.Rendering.ShaderPropertyType.Range:
                            own.SetFloat(id, Mathf.Lerp(from.GetFloat(id), to.GetFloat(id), t));
                            break;
                    }
                }
                if (image.material != own) image.material = own;
            }

            /// <summary>
            /// The light rose (or fell) by more than a tile's burn: DD2's widget flares as it does in a fight.
            /// Its timeline moves the glow and the embers only; the bars are the caller's (in DD2 too: its code
            /// steps them once the timeline has said "now").
            /// </summary>
            public void Flare(bool up)
            {
                if (!Flares || Root == null || !Root.gameObject.activeInHierarchy) return;
                try
                {
                    var asset = up ? _increase : _decrease;
                    if (_timeline != null && asset != null)
                    {
                        _timeline.Stop();
                        _timeline.time = 0.0;
                        _timeline.Play(asset);
                    }
                    var burst = up ? _burstUp : _burstDown;
                    if (burst != null)
                    {
                        burst.Stop();
                        burst.time = 0.0;
                        burst.Play();
                    }
                    Flared++;
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("DD2 torch: its flare could not be played (" + e.Message + "); left out from here on");
                    Flares = false;
                }
            }

            public object Describe()
            {
                return new
                {
                    flares = Flares, flared = Flared, timeline = _timeline != null ? _timeline.state + " " + (_timeline.playableAsset != null ? _timeline.playableAsset.name : "none") + " at " + _timeline.time.ToString("0.00") : null,
                    bursts = new[] { _burstUp != null, _burstDown != null },
                    shown = _shown, level = _level, from = _from, mix = _mix, changes = Changes, levelTime = LevelTime,
                    thresholds = new[] { _low, _medium, _high },
                    flame = _flameImage != null && _flameImage.material != null ? _flameImage.material.name : null,
                    bar = _rightFill != null && _rightFill.material != null ? _rightFill.material.name : null,
                    right = _rightBar != null ? _rightBar.value : -1f, left = _leftBar != null ? _leftBar.value : -1f
                };
            }

            public void Destroy()
            {
                if (_flameOwn != null) UnityEngine.Object.Destroy(_flameOwn);
                if (_fillOwn != null) UnityEngine.Object.Destroy(_fillOwn);
                if (Root != null) UnityEngine.Object.Destroy(Root.gameObject);
            }
        }
    }
}
