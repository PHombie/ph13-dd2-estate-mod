using System;
using System.Collections.Generic;
using Assets.Code.Actor;
using Assets.Code.UI;
using Assets.Code.Utils;
using DD2Estate.Estate;
using DD2Estate.UI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// A skill as DD2's fight shows it, for the banner's row of skills (the owner, 2026-10-06, of DD1's grey
    /// ability frames there: "remove the grey frame on the skills and make it the same as in a fight").
    ///
    /// It is a copy of the fight's own skill button: the prefab the fight's skill bar spawns its buttons from
    /// (SkillSelectionBhv.m_SkillButtonPrefab, combat_skill_button: <see cref="SelectableSkillButton"/>), found
    /// in the prefab of the fight's HUD (<see cref="Dd2Hud"/>). The button is a box of 100 on a 1080 px high
    /// screen and holds, from the ground up:
    /// - BG: the class's own ground (ResourceActor.m_SkillIconBG, skill_icon_&lt;class&gt;_background): a black
    ///   square with a thin broken frame line drawn into it, a little smaller than the box;
    /// - SkillIcon: the skill's picture at 105, larger than the frame: DD2's skill pictures reach over it;
    /// - overlay_selected_image: the two gold corners under a chosen skill (ui_combat_skill_indicator);
    /// - overly_hovered_image: the two light corners across a pointed-at one (ui_frame_highlight, turned 45
    ///   degrees so that they stand on the frame's top left and bottom right corner);
    /// - a skill that cannot be used is drawn through the game's greyscale material, darkened
    ///   (SelectableSkillButton.RefreshInternal).
    /// The copy is stripped of the game's behaviours (they listen to the fight) and of what a fight alone has a
    /// use for (cooldown, hotkey, the marks of a relationship), and <see cref="View"/> drives the rest.
    ///
    /// In the banner the box is as wide as the row's step from skill to skill (DD1's 76), so the skills stand
    /// in a fight's own proportions: frame, gap and picture.
    /// </summary>
    [EstateModule]
    internal static class Dd2Skills
    {
        /// <summary>[Look] BannerSkills: "dd2" the banner's skills in the frame of DD2's fights, "dd1" in DD1's grey frames.</summary>
        public static bool Enabled = true;

        /// <summary>The side of the game's button, in its own pixels.</summary>
        public const float Box = 100f;
        /// <summary>Where the frame line begins and ends inside the class's ground, as a share of the ground's side (11 and 20 of the picture's 403 pixels).</summary>
        public const float LineOuter = 11f / 403f, LineInner = 20f / 403f;
        // FALLBACK for a ground whose own margins cannot be asked for: the picture is 403 of a sprite of 450
        private const float GroundInsetStock = 23.5f / 450f;

        private static GameObject _template;
        private static bool _failed;
        private static int[] _ground, _icon, _selected, _hovered;
        private static Material _grey;
        // SelectableSkillButton's own numbers for a skill that cannot be used
        private static readonly Color Darkened = new Color32(92, 92, 92, 255);

        public static bool Ready => _template != null;
        public static string Status { get; private set; } = "not asked for";

        /// <summary>The parts of the game's button that the narrow place of "move" is made of too: what marks the chosen and the pointed-at skill.</summary>
        public static Sprite SelectedSprite { get; private set; }
        public static Color SelectedColour { get; private set; } = new Color32(0xAA, 0x8D, 0x5B, 0xFF);
        /// <summary>Its box in the button (the picture keeps its own proportions in it) and its middle above the button's foot.</summary>
        public static Vector2 SelectedSize { get; private set; } = new Vector2(100f, 25f);
        public static float SelectedRise { get; private set; } = 10f;
        public static Sprite HoverSprite { get; private set; }
        public static Color HoverColour { get; private set; } = new Color32(0xCF, 0xCF, 0xCF, 0xFF);
        public static Vector2 HoverSize { get; private set; } = new Vector2(144f, 38f);
        public static float HoverAngle { get; private set; } = 315f;

        /// <summary>
        /// [Look] BannerSkillsGrey: outside a fight the banner's combat skills are drawn as a fight draws a skill
        /// that cannot be used (grey and dark). The owner's word, 2026-10-06, after seeing them in colour: "After all,
        /// make these icons in the hero panel during a dungeon grey, as when a hero's skill is unavailable
        /// during a fight". (DD1's own dark grey there had been taken out at their word the day before: it is
        /// DD2's look of an unusable skill they want, in DD2's frames.)
        /// </summary>
        public static bool GreyOutsideAFight { get; private set; } = true;

        private static void Register()
        {
            GreyOutsideAFight = Plugin.Settings.Bind("Look", "BannerSkillsGrey", true,
                "Outside a fight the hero's combat skills in the dungeon banner are grey, as a fight draws a skill that cannot be used now. Off: in their own colours.").Value;
            Enabled = !string.Equals(Plugin.Settings.Bind("Look", "BannerSkills", "dd2",
                "The skills in the dungeon banner (and \"move\" beside them): dd2 = in the frame of DD2's fights (the class's dark ground, the thin frame, DD2's marks for a chosen and a pointed-at skill), dd1 = in DD1's grey ability frames.").Value.Trim(), "dd1", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Asks for the button; it is there some frames later. Called every frame while the Estate is up.</summary>
        public static void Prepare()
        {
            if (_template != null || _failed || !Enabled) return;
            try
            {
                var hud = Dd2Hud.FightHud(out var waiting);
                if (hud == null)
                {
                    if (waiting) Status = "loading";
                    else Fail("the game does not hand over the fight's HUD");
                    return;
                }
                GameObject source = null;
                foreach (var bar in hud.GetComponentsInChildren<SkillSelectionBhv>(true))
                {
                    var prefab = AccessTools.Field(typeof(SkillSelectionBhv), "m_SkillButtonPrefab")?.GetValue(bar) as GameObject;
                    if (prefab == null || prefab.GetComponent<SelectableSkillButton>() == null) continue;
                    source = prefab;
                    break;
                }
                if (source == null) Fail("the fight's HUD has no skill button");
                else Capture(source);
            }
            catch (Exception e) { Fail(e.Message); }
        }

        private static void Fail(string why)
        {
            _failed = true;
            Status = "failed: " + why;
            Plugin.Log.LogWarning("DD2 skills: " + why + "; the banner's skills stay in DD1's frames");
            if (_template != null) UnityEngine.Object.Destroy(_template);
            _template = null;
        }

        /// <summary>Dev bridge: the copy is thrown away and made again.</summary>
        public static void Reset()
        {
            if (_template != null) UnityEngine.Object.Destroy(_template);
            _template = null;
            _failed = false;
            Status = "asked for again";
        }

        private static T F<T>(object target, string name) where T : class
        {
            return target != null ? AccessTools.Field(target.GetType(), name)?.GetValue(target) as T : null;
        }

        private static void Capture(GameObject source)
        {
            // Under a switched-off parent nothing of the copy wakes up until it is put on screen.
            var clone = UnityEngine.Object.Instantiate(source, Dd2Hud.Holder, false);
            try
            {
                clone.name = "Dd2Skill";
                clone.SetActive(true);
                var root = clone.transform;
                var button = clone.GetComponent<SelectableSkillButton>();
                var icon = F<Image>(button, "m_SkillImage");
                var ground = F<Image>(button, "m_bgImage");
                var selected = F<GameObject>(button, "m_OverlaySelectedImage");
                var hovered = F<GameObject>(button, "m_OverlayHoveredImage");
                if (icon == null || ground == null) throw new InvalidOperationException("the button has no picture or no ground");
                _grey = F<Material>(button, "m_GreyscaleMaterial");

                // what only a fight has a use for goes off: the direct parts that are neither the ground, the picture nor a mark
                var kept = new HashSet<Transform> { ground.transform, icon.transform };
                if (selected != null) kept.Add(selected.transform);
                if (hovered != null) kept.Add(hovered.transform);
                for (var i = 0; i < root.childCount; i++)
                {
                    var child = root.GetChild(i);
                    var stays = false;
                    foreach (var part in kept) stays |= part == child || part.IsChildOf(child);
                    if (!stays) child.gameObject.SetActive(false);
                }
                ground.gameObject.SetActive(true);
                icon.gameObject.SetActive(true);
                ground.overrideSprite = null;
                icon.overrideSprite = null;
                icon.sprite = null;
                icon.enabled = false;
                icon.material = null;
                ground.material = null;
                icon.color = ground.color = Color.white;
                if (selected != null)
                {
                    selected.SetActive(false);
                    var image = selected.GetComponent<Image>();
                    var rect = selected.transform as RectTransform;
                    if (image != null && rect != null)
                    {
                        SelectedSprite = image.sprite;
                        SelectedColour = image.color;
                        SelectedSize = rect.sizeDelta;
                        // its middle above the button's foot, whatever it is anchored to
                        SelectedRise = rect.anchoredPosition.y + rect.anchorMin.y * Box;
                    }
                }
                if (hovered != null)
                {
                    hovered.SetActive(false);
                    var image = hovered.GetComponent<Image>();
                    var rect = hovered.transform as RectTransform;
                    if (image != null && rect != null)
                    {
                        HoverSprite = image.sprite;
                        HoverColour = image.color;
                        HoverSize = rect.sizeDelta;
                        HoverAngle = rect.localEulerAngles.z;
                    }
                }
                _ground = Dd2Hud.PathTo(root, ground.transform);
                _icon = Dd2Hud.PathTo(root, icon.transform);
                _selected = selected != null ? Dd2Hud.PathTo(root, selected.transform) : null;
                _hovered = hovered != null ? Dd2Hud.PathTo(root, hovered.transform) : null;

                var removed = Dd2Hud.Strip(clone, false);
                foreach (var group in clone.GetComponentsInChildren<CanvasGroup>(true))
                    if (group.gameObject == clone) group.alpha = 1f;
                _template = clone;
                Status = "ready";
                Plugin.Log.LogInfo("DD2 skills: the fight's skill button is copied (behaviours left out: " + string.Join(", ", removed) + ")");
            }
            catch (Exception)
            {
                UnityEngine.Object.Destroy(clone);
                throw;
            }
        }

        /// <summary>The ground DD2 draws under the skills of a hero's class; null when the class has none.</summary>
        public static Sprite GroundOf(ActorInstance actor)
        {
            if (actor == null) return null;
            try
            {
                var resource = Singleton<ResourceDatabaseActors>.Instance.GetResource(actor.ActorDataId, isErrorValid: false);
                return resource != null ? resource.m_SkillIconBG : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// How far the ground's picture (the black square) stands inside the button's box on either side, as a
        /// share of the box: the sprite is the picture with a clear margin around it.
        /// </summary>
        public static float GroundInset(Sprite ground)
        {
            if (ground == null || ground.rect.width <= 0f) return GroundInsetStock;
            try
            {
                var padding = DataUtility.GetPadding(ground);       // left, bottom, right, top
                return Mathf.Clamp((padding.x + padding.z) * 0.5f / ground.rect.width, 0f, 0.3f);
            }
            catch (Exception)
            {
                return GroundInsetStock;
            }
        }

        /// <summary>How far the frame line's outer edge stands inside the button's box, as a share of the box.</summary>
        public static float FrameInset(Sprite ground)
        {
            var inset = GroundInset(ground);
            return inset + (1f - 2f * inset) * LineOuter;
        }

        /// <summary>
        /// A skill on screen under <paramref name="parent"/>, its middle at the parent's origin (the caller puts
        /// it where it belongs), the button's box <paramref name="side"/> wide; null while the copy is not there.
        /// </summary>
        public static View Create(Transform parent, float side)
        {
            if (!Ready) return null;
            try
            {
                var root = (RectTransform)UnityEngine.Object.Instantiate(_template, parent, false).transform;
                root.name = "Dd2Skill";
                root.anchorMin = root.anchorMax = new Vector2(0f, 1f);
                root.pivot = new Vector2(0.5f, 0.5f);
                root.sizeDelta = new Vector2(Box, Box);
                root.anchoredPosition = Vector2.zero;
                root.localRotation = Quaternion.identity;
                root.localScale = Vector3.one * (side / Box);
                root.gameObject.SetActive(true);
                return new View(root);
            }
            catch (Exception e)
            {
                Fail("a skill could not be put on screen (" + e.Message + ")");
                return null;
            }
        }

        // A picture with a frame of its own drawn into it, without that frame.
        private static readonly Dictionary<Sprite, Sprite> Unframed = new Dictionary<Sprite, Sprite>();

        private static Sprite WithoutRim(Sprite picture, float rim)
        {
            if (picture == null || rim <= 0f) return picture;
            if (Unframed.TryGetValue(picture, out var known) && known != null) return known;
            try
            {
                var rect = picture.textureRect;
                float dx = rect.width * rim, dy = rect.height * rim;
                var inner = Sprite.Create(picture.texture, new Rect(rect.x + dx, rect.y + dy, rect.width - 2f * dx, rect.height - 2f * dy), new Vector2(0.5f, 0.5f), picture.pixelsPerUnit, 0, SpriteMeshType.FullRect);
                inner.name = picture.name + ".inner";
                inner.hideFlags = HideFlags.HideAndDontSave;
                Unframed[picture] = inner;
                return inner;
            }
            catch (Exception)
            {
                return picture;
            }
        }

        /// <summary>One skill on screen.</summary>
        internal class View
        {
            public readonly RectTransform Root;
            private readonly Image _groundImage, _iconImage;
            private readonly GameObject _selectedMark, _hoveredMark;
            private Image _framed;
            private bool _dim, _chosen, _pointed;

            public View(RectTransform root)
            {
                Root = root;
                _groundImage = Dd2Hud.At<Image>(root, _ground);
                _iconImage = Dd2Hud.At<Image>(root, _icon);
                var selected = Dd2Hud.At(root, _selected);
                var hovered = Dd2Hud.At(root, _hovered);
                _selectedMark = selected != null ? selected.gameObject : null;
                _hoveredMark = hovered != null ? hovered.gameObject : null;
            }

            /// <summary>What takes the pointer for the skill: the button's whole box.</summary>
            public Graphic Area => _groundImage;

            /// <summary>The class's ground under the skill; none: the button's own stays.</summary>
            public void Ground(Sprite ground)
            {
                if (_groundImage == null) return;
                _groundImage.overrideSprite = ground;
                _groundImage.enabled = _groundImage.overrideSprite != null || _groundImage.sprite != null;
            }

            /// <summary>A skill picture of DD2's, at the size the fight draws it: larger than the frame. None: the empty frame.</summary>
            public void Icon(Sprite picture)
            {
                if (_framed != null) _framed.gameObject.SetActive(false);
                if (_iconImage == null) return;
                _iconImage.sprite = picture;
                _iconImage.enabled = picture != null;
                Paint();
            }

            /// <summary>
            /// A picture that is a framed square of its own (DD1's camping skills): shown inside DD2's frame line,
            /// filling it, without its own rim (<paramref name="rim"/>: the rim's share of the picture's side).
            /// </summary>
            public void FramedIcon(Sprite picture, float rim)
            {
                if (_iconImage != null) _iconImage.enabled = false;
                if (_framed == null && _iconImage != null)
                {
                    var rect = (RectTransform)new GameObject("Framed", typeof(RectTransform)).transform;
                    rect.SetParent(_iconImage.transform.parent, false);
                    rect.gameObject.layer = _iconImage.gameObject.layer;
                    rect.SetSiblingIndex(_iconImage.transform.GetSiblingIndex() + 1);
                    rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                    rect.anchoredPosition = Vector2.zero;
                    _framed = rect.gameObject.AddComponent<Image>();
                    _framed.raycastTarget = false;
                }
                if (_framed == null) return;
                var ground = _groundImage != null ? _groundImage.overrideSprite ?? _groundImage.sprite : null;
                var inset = GroundInset(ground);
                // inside the frame line
                var inner = Box * (1f - 2f * inset) * (1f - 2f * LineInner);
                ((RectTransform)_framed.transform).sizeDelta = new Vector2(inner, inner);
                _framed.sprite = WithoutRim(picture, rim);
                _framed.gameObject.SetActive(picture != null);
                Paint();
            }

            /// <summary>A skill that cannot be used now: grey and dark, as a fight draws one.</summary>
            public bool Dim
            {
                set
                {
                    _dim = value;
                    Paint();
                }
            }

            // SelectableSkillButton.RefreshInternal
            private void Paint()
            {
                foreach (var image in new[] { _iconImage, _framed })
                {
                    if (image == null) continue;
                    image.material = _dim ? _grey : null;
                    image.color = _dim ? Darkened : Color.white;
                }
                if (_groundImage == null) return;
                _groundImage.material = _dim ? _grey : null;
                _groundImage.color = _dim ? Color.grey : Color.white;
            }

            /// <summary>The chosen skill: DD2's gold corners under it (the glow around it is a flash as it is chosen, gone a moment later).</summary>
            public bool Chosen
            {
                set
                {
                    _chosen = value;
                    Marks();
                }
            }

            /// <summary>The skill under the pointer: DD2's light corners across it, on a chosen skill as well (seen in a fight).</summary>
            public bool Pointed
            {
                set
                {
                    _pointed = value;
                    Marks();
                }
            }

            private void Marks()
            {
                if (_selectedMark != null && _selectedMark.activeSelf != _chosen) _selectedMark.SetActive(_chosen);
                if (_hoveredMark != null && _hoveredMark.activeSelf != _pointed) _hoveredMark.SetActive(_pointed);
            }

            /// <summary>Dev bridge: what the skill shows.</summary>
            public object Describe()
            {
                var ground = _groundImage != null ? _groundImage.overrideSprite ?? _groundImage.sprite : null;
                var picture = _framed != null && _framed.gameObject.activeSelf ? _framed.sprite : _iconImage != null && _iconImage.enabled ? _iconImage.sprite : null;
                return new
                {
                    ground = ground != null ? ground.name : null, picture = picture != null ? picture.name : null,
                    framed = _framed != null && _framed.gameObject.activeSelf, dim = _dim, chosen = _chosen, pointed = _pointed,
                    side = Root.localScale.x * Box, groundInset = GroundInset(ground), frameInset = FrameInset(ground),
                    box = DungeonLookDev.Box(Root)
                };
            }
        }

        // ---- parts of a picture, for the narrow place of "move" ----------------------------------------------

        /// <summary>
        /// A picture with the middle of its width left out: its left and its right half-width side by side in a
        /// box <paramref name="width"/> wide, drawn at <paramref name="drawn"/> (the size the whole picture has
        /// elsewhere). <paramref name="skip"/> of the picture's left and right edge is left out as well. The
        /// box's top left corner stands at <paramref name="at"/> (y down). No pixel is read: each half is the
        /// whole picture behind a window.
        /// </summary>
        public static RectTransform Halves(string name, Transform parent, Sprite picture, Color colour, Vector2 at, Vector2 drawn, float width, float skip)
        {
            var root = UiKit.Rect(name, parent);
            root.PlaceTopLeft(at, new Vector2(0f, 1f), new Vector2(width, drawn.y));
            for (var side = 0; side < 2; side++)
            {
                var window = UiKit.Rect(side == 0 ? "Left" : "Right", root);
                window.PlaceTopLeft(new Vector2(side == 0 ? 0f : width * 0.5f, 0f), new Vector2(0f, 1f), new Vector2(width * 0.5f, drawn.y));
                window.gameObject.AddComponent<RectMask2D>();
                var image = UiKit.Image("Picture", window, picture, colour);
                ((RectTransform)image.transform).PlaceTopLeft(new Vector2(side == 0 ? -skip : width * 0.5f - drawn.x + skip, 0f), new Vector2(0f, 1f), drawn);
            }
            return root;
        }

        /// <summary>
        /// One of the two corners DD2 marks a pointed-at skill with, on a corner of a frame that is not DD2's
        /// square: the fight's picture of both corners, turned as the fight turns it, behind a window on the
        /// corner. <paramref name="corner"/>: the frame's corner (y down); <paramref name="far"/>: the bottom
        /// right one (else the top left); <paramref name="half"/>: half the side of the square frame the picture
        /// was drawn for, at this scale; <paramref name="scale"/>: the banner's pixels per pixel of the button.
        /// </summary>
        public static RectTransform Corner(string name, Transform parent, Vector2 corner, bool far, float half, float scale)
        {
            var reach = half * 0.8f;
            var window = UiKit.Rect(name, parent);
            window.PlaceTopLeft(corner - new Vector2(reach, reach), new Vector2(0f, 1f), new Vector2(2f * reach, 2f * reach));
            window.gameObject.AddComponent<RectMask2D>();
            var image = UiKit.Image("Picture", window, HoverSprite, HoverColour);
            var rect = (RectTransform)image.transform;
            // the picture's middle is the middle of the square it marks: half a side from the corner, inwards
            rect.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), far ? new Vector2(-half, half) : new Vector2(half, -half), HoverSize * scale);
            rect.localRotation = Quaternion.Euler(0f, 0f, HoverAngle);
            return window;
        }
    }
}
