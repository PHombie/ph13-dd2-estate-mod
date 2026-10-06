using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using DD2Estate.Dd1;
using DD2Estate.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// DD1's own words for its raid screen, from the English section of the player's string tables (the mod
    /// carries no DD1 text). Only the entries the raid screens use are kept. A text of the tables may carry
    /// DD1's colour marks ({colour_start|name}...{colour_end}) and printf places (%d, %s, %.0f, %+d%%):
    /// <see cref="Format"/> fills the places and turns the marks into TextMeshPro's.
    /// </summary>
    internal static class RaidText
    {
        private static readonly string[] Tables =
        {
            "localization/miscellaneous.string_table.xml", "localization/curios.string_table.xml",
            // one entry of the heroes' table: the name of the "move" skill
            "localization/heroes.string_table.xml"
        };

        private static readonly string[] Wanted =
        {
            "\"str_ui_", "\"str_overlay_loot_", "\"str_darkness_", "\"str_obstacle_", "\"obstacle_tooltip_", "\"raid_quest_complete\"",
            "\"str_quest_complete_banner\"", "\"retreat_", "\"str_continue_raid_tooltip\"", "\"raid_results_", "\"resolve_bar_tooltip_",
            "\"str_inventory_description_", "\"str_inventory_gold_value_format\"",
            "\"str_centre_map_tooltip\"", "\"str_map_", "\"not_enough_room", "\"str_discard_", "\"cant_discard_quest_item_confirm\"",
            "\"str_move_to_this_room\"", "\"provision_sell_back_info\"", "\"town_name_provision\"", "\"town_progression_forward_",
            "\"str_cant_use_", "\"str_meal_", "\"camping_respite_", "\"dungeon_name_", "\"town_quest_length_", "\"town_quest_difficulty_",
            "\"town_provision_", "\"str_curio_title_", "\"str_curio_content_", "\"str_curio_tooltip_investigate_", "\"tray_status_bar_tooltip_",
            "\"town_quest_goal_start_", "\"curio_tooltip_", "\"str_inventory_gold_value_format\"",
            // the banners, the words that pop up over a hero, the status icons' tooltips
            "\"str_its_a_trap\"", "\"str_ambush_title\"", "\"surprise_announcement\"", "\"str_new_quirk_colon\"", "\"curio_announcement_purge_format\"",
            "\"str_curio_no_negative_quirks_to_remove\"", "\"str_full\"", "\"tray_icon_tooltip_", "\"buff_stat_tooltip_", "\"buff_rule_tooltip_",
            "\"buff_bsrc_deathsdoor", "\"str_diseases\"",
            // what an interaction with a curio did (str_curio_<curio>[_<item>]_<result>), the line of user
            // information, a hero's chance at a trap, the party-order button, the way home
            "\"str_curio_", "\"str_user_information_", "\"resistance_trap_disarm_format\"", "\"str_party_default_order\"", "\"str_return_to_hamlet_tooltip\"",
            "\"combat_skill_name_crusader_move\""
        };

        private static readonly Regex Entry = new Regex("<entry id=\"([^\"]+)\"><!\\[CDATA\\[(.*?)\\]\\]></entry>");
        private static readonly Regex Place = new Regex(@"%([+-]?)(?:\.(\d))?([dsf%])");
        private static readonly Regex Named = new Regex(@"\{\?[^}]*\}");
        private static readonly Regex ColourStart = new Regex(@"\{colour_start\|([^}]+)\}");
        private static Dictionary<string, string> _strings;

        /// <summary>DD1's English text by id; <paramref name="fallback"/> when the install has none.</summary>
        public static string Get(string id, string fallback = null)
        {
            if (_strings == null)
            {
                // Asked before the install is found, nothing is kept: the next call reads again.
                if (!Dd1Install.Found) return fallback;
                _strings = Read();
            }
            return _strings.TryGetValue(id, out var text) && !string.IsNullOrEmpty(text) ? text : fallback;
        }

        /// <summary>
        /// A DD1 text with its printf places filled in order and its colour marks made TextMeshPro's. A number
        /// is given as a number: "%+d" writes its sign, "%.0f" rounds it. A number that must keep its decimals
        /// (DD1's places are for whole ones) is given written out, and goes in as it is.
        /// </summary>
        public static string Format(string text, params object[] values)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var next = 0;
            text = Named.Replace(text, "");
            text = Place.Replace(text, m =>
            {
                if (m.Groups[3].Value == "%") return "%";
                if (next >= values.Length) return "";
                var value = values[next++];
                if (m.Groups[3].Value == "s") return Convert.ToString(value, CultureInfo.InvariantCulture);
                if (value is string ready)
                    return m.Groups[1].Value == "+" && !ready.StartsWith("-", StringComparison.Ordinal) && !ready.StartsWith("+", StringComparison.Ordinal) ? "+" + ready : ready;
                var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                var digits = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
                var written = Math.Round(number, digits, MidpointRounding.AwayFromZero).ToString("F" + digits, CultureInfo.InvariantCulture);
                return m.Groups[1].Value == "+" && number >= 0 ? "+" + written : written;
            });
            return Marks(text);
        }

        /// <summary>DD1's colour marks as TextMeshPro colour tags (colours/base.colours.darkest names the colours).</summary>
        public static string Marks(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf("{colour", StringComparison.Ordinal) < 0) return text ?? "";
            text = ColourStart.Replace(text, m => "<color=#" + ColorUtility.ToHtmlStringRGB(Dd1Fonts.Colour(m.Groups[1].Value, UiKit.Neutral)) + ">");
            return text.Replace("{colour_end}", "</color>");
        }

        public static string Hex(Color colour) => "#" + ColorUtility.ToHtmlStringRGB(colour);

        // Each file holds every language; English comes first, so reading stops at the end of its section.
        private static Dictionary<string, string> Read()
        {
            var strings = new Dictionary<string, string>();
            foreach (var table in Tables)
            {
                var path = Dd1Install.PathOf(table);
                if (path == null || !File.Exists(path)) continue;
                try
                {
                    var english = false;
                    foreach (var line in File.ReadLines(path))
                    {
                        if (line.IndexOf("<language ", StringComparison.Ordinal) >= 0)
                        {
                            if (english) break;
                            english = line.IndexOf("id=\"english\"", StringComparison.Ordinal) >= 0;
                            continue;
                        }
                        if (!english) continue;
                        if (line.IndexOf("</language>", StringComparison.Ordinal) >= 0) break;
                        var wanted = false;
                        foreach (var prefix in Wanted)
                            if (line.IndexOf(prefix, StringComparison.Ordinal) >= 0)
                            {
                                wanted = true;
                                break;
                            }
                        if (!wanted) continue;
                        var match = Entry.Match(line);
                        if (match.Success && !strings.ContainsKey(match.Groups[1].Value)) strings[match.Groups[1].Value] = match.Groups[2].Value;
                    }
                }
                catch (Exception e) { Plugin.Log.LogWarning("DD1 string table could not be read (" + table + "): " + e.Message); }
            }
            return strings;
        }
    }

    /// <summary>Builders the raid screens share: DD1 art, DD1 text styles, DD1's tooltip box.</summary>
    internal static class RaidUi
    {
        public static readonly Vector2 TopLeft = new Vector2(0f, 1f);
        public static readonly Vector2 TopCentre = new Vector2(0.5f, 1f);
        public static readonly Vector2 TopRight = new Vector2(1f, 1f);
        public static readonly Vector2 Middle = new Vector2(0.5f, 0.5f);

        private static readonly Dictionary<string, Sprite> SlicedSprites = new Dictionary<string, Sprite>();

        /// <summary>DD1's 1920x1080 screen on a canvas of any width: centred, standing on the canvas' foot.</summary>
        public static RectTransform Screen(string name, Transform canvas)
        {
            var screen = UiKit.Rect(name, canvas);
            screen.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(1920f, 1080f));
            return screen;
        }

        /// <summary>
        /// A piece of DD1 art at its own size (or <paramref name="size"/>) with its top left corner at
        /// <paramref name="at"/>; hidden when the file is missing, or a flat box where something must show.
        /// </summary>
        public static Image Art(string name, Transform parent, string dd1File, Vector2 at, Vector2? size = null, Color? fallback = null, bool raycast = false)
        {
            var sprite = Sprite(dd1File);
            var image = UiKit.Image(name, parent, sprite, sprite != null ? Color.white : fallback ?? Color.clear, raycast);
            ((RectTransform)image.transform).PlaceTopLeft(at, TopLeft, size ?? (sprite != null ? sprite.rect.size : Vector2.zero));
            return image;
        }

        /// <summary>A DD1 picture; null (without a line in the log) when the install or the file is missing.</summary>
        public static Sprite Sprite(string dd1File)
        {
            return !string.IsNullOrEmpty(dd1File) && Dd1Install.Found && Dd1Install.Exists(dd1File) ? Dd1Install.Sprite(dd1File) : null;
        }

        /// <summary>A DD1 picture cut in nine so that it can frame a box of any size (the tooltip's ground).</summary>
        public static Sprite Sliced(string dd1File, float border)
        {
            if (SlicedSprites.TryGetValue(dd1File, out var cached) && cached != null) return cached;
            if (!Dd1Install.Found || !Dd1Install.Exists(dd1File)) return null;
            var texture = Dd1Install.Texture(dd1File);
            if (texture == null) return null;
            var sprite = UnityEngine.Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), Middle, 100f, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
            sprite.name = dd1File + ".sliced";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            SlicedSprites[dd1File] = sprite;
            return sprite;
        }

        private static readonly Dictionary<long, Sprite> Drained = new Dictionary<long, Sprite>();
        private static Dictionary<string, string> _colourLines;

        /// <summary>
        /// A number DD1's colour table gives a named colour beside its colour: the ".saturation" of
        /// inventory_unselectable (0.2), the ".darkness" of skill_unselectable (0.4: what is left of the picture's
        /// light, measured in DD1's own frame: a combat skill outside a fight is its art in grey at 0.40).
        /// </summary>
        public static float ColourNumber(string id, string key, float fallback)
        {
            if (_colourLines == null)
            {
                if (!Dd1Install.Found) return fallback;
                _colourLines = new Dictionary<string, string>();
                try
                {
                    var text = Dd1Install.ReadText("colours/base.colours.darkest") ?? "";
                    foreach (Match line in Regex.Matches(text, "\\.id\\s+\"([^\"]+)\"([^\\r\\n]*)"))
                        if (!_colourLines.ContainsKey(line.Groups[1].Value)) _colourLines[line.Groups[1].Value] = line.Groups[2].Value;
                }
                catch (Exception e) { Plugin.Log.LogWarning("DD1's colour table could not be read for its saturations: " + e.Message); }
            }
            if (id == null || !_colourLines.TryGetValue(id, out var rest)) return fallback;
            var match = Regex.Match(rest, "\\." + key + "\\s+([0-9.]+)");
            return match.Success && float.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;
        }

        /// <summary>
        /// A copy of a picture with its colours drawn towards grey: DD1 draws what cannot be used with a
        /// saturation of its own (colours/base.colours.darkest: inventory_unselectable .saturation 0.2,
        /// skill_unselectable .saturation 0.0), which a UI picture's tint cannot do. The copy is made once per
        /// picture and saturation; null when it cannot be made (the caller keeps the picture as it is).
        /// </summary>
        public static Sprite Desaturated(Sprite source, float saturation)
        {
            if (source == null) return null;
            saturation = Mathf.Clamp01(saturation);
            if (saturation >= 0.999f) return source;
            var key = ((long)source.GetInstanceID() << 8) ^ Mathf.RoundToInt(saturation * 100f);
            if (Drained.TryGetValue(key, out var made)) return made;
            Sprite sprite = null;
            RenderTexture target = null;
            var active = RenderTexture.active;
            try
            {
                // through a render target: the game's own pictures cannot be read where they lie
                var texture = source.texture;
                var rect = source.textureRect;
                target = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(texture, target);
                RenderTexture.active = target;
                var copy = new Texture2D(Mathf.RoundToInt(rect.width), Mathf.RoundToInt(rect.height), TextureFormat.RGBA32, false)
                {
                    wrapMode = TextureWrapMode.Clamp, filterMode = texture.filterMode, hideFlags = HideFlags.HideAndDontSave, name = source.name + ".drained"
                };
                copy.ReadPixels(new Rect(rect.x, rect.y, rect.width, rect.height), 0, 0);
                var pixels = copy.GetPixels32();
                for (var i = 0; i < pixels.Length; i++)
                {
                    var p = pixels[i];
                    var grey = 0.299f * p.r + 0.587f * p.g + 0.114f * p.b;
                    pixels[i] = new Color32((byte)(grey + (p.r - grey) * saturation), (byte)(grey + (p.g - grey) * saturation), (byte)(grey + (p.b - grey) * saturation), p.a);
                }
                copy.SetPixels32(pixels);
                copy.Apply(false, true);
                sprite = UnityEngine.Sprite.Create(copy, new Rect(0f, 0f, copy.width, copy.height), new Vector2(source.pivot.x / Mathf.Max(1f, rect.width), source.pivot.y / Mathf.Max(1f, rect.height)), source.pixelsPerUnit);
                sprite.name = source.name + ".drained";
                sprite.hideFlags = HideFlags.HideAndDontSave;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Dungeon HUD: the picture " + source.name + " could not be drawn without its colours: " + e.Message);
                sprite = null;
            }
            finally
            {
                RenderTexture.active = active;
                if (target != null) RenderTexture.ReleaseTemporary(target);
            }
            Drained[key] = sprite;
            return sprite;
        }

        /// <summary>
        /// A label in a DD1 text style, drawn 1:1, its line cell's top at <paramref name="at"/>.y. The x is the
        /// left edge, the middle or the right edge of the text by the alignment asked for.
        /// </summary>
        public static TextMeshProUGUI Label(string name, Transform parent, string style, Vector2 at, Vector2 size, TextAlignmentOptions align = TextAlignmentOptions.TopLeft, Color? colour = null)
        {
            var label = UiKit.Text(name, parent, "", style, colour, align);
            var pivot = align == TextAlignmentOptions.Top ? TopCentre : align == TextAlignmentOptions.TopRight ? TopRight : TopLeft;
            ((RectTransform)label.transform).PlaceTopLeft(at, pivot, size);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            return label;
        }

        /// <summary>A label that wraps inside its width and, when it still does not fit its height, shrinks.</summary>
        public static TextMeshProUGUI Paragraph(string name, Transform parent, string style, Vector2 at, Vector2 size, TextAlignmentOptions align = TextAlignmentOptions.TopLeft, Color? colour = null, float smallest = 13f)
        {
            var label = Label(name, parent, style, at, size, align, colour);
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Truncate;
            label.enableAutoSizing = true;
            label.fontSizeMax = label.fontSize;
            label.fontSizeMin = Mathf.Min(smallest, label.fontSize);
            return label;
        }

        /// <summary>One line that shrinks to stay inside its box: DD1's fonts are wider than DD2's.</summary>
        public static TextMeshProUGUI Fit(TextMeshProUGUI label, float smallest)
        {
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Truncate;
            label.enableAutoSizing = true;
            label.fontSizeMax = label.fontSize;
            label.fontSizeMin = Mathf.Min(smallest, label.fontSize);
            return label;
        }

        /// <summary>The same text again in black, a pixel and a half down and right: for numbers drawn on bright art.</summary>
        public static TextMeshProUGUI Shadow(TextMeshProUGUI label)
        {
            var shadow = UnityEngine.Object.Instantiate(label.gameObject, label.transform.parent).GetComponent<TextMeshProUGUI>();
            shadow.name = label.name + "Shadow";
            shadow.color = Color.black;
            var rt = (RectTransform)shadow.transform;
            rt.anchoredPosition = ((RectTransform)label.transform).anchoredPosition + new Vector2(1.5f, -1.5f);
            shadow.transform.SetSiblingIndex(label.transform.GetSiblingIndex());
            return shadow;
        }

        /// <summary>
        /// A button made of a DD1 picture. As in DD1 it rests as its art is and is brighter under the pointer
        /// (colours/base.colours.darkest, button_highlight: <see cref="RaidHighlight"/>). <paramref name="lit"/>:
        /// a button with a glow of its own for the pointer, which is not brightened as well.
        /// </summary>
        public static Button ArtButton(string name, Transform parent, string dd1File, Vector2 at, Action onClick, Vector2? size = null, bool lit = false)
        {
            var image = Art(name, parent, dd1File, at, size, new Color(0.25f, 0.2f, 0.12f), true);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colours = button.colors;
            colours.normalColor = Color.white;
            colours.highlightedColor = Color.white;
            colours.pressedColor = new Color(0.7f, 0.6f, 0.5f);
            colours.selectedColor = Color.white;
            colours.disabledColor = new Color(0.32f, 0.32f, 0.32f);
            colours.fadeDuration = 0.05f;
            button.colors = colours;
            if (!lit) image.gameObject.AddComponent<RaidHighlight>().Button = button;
            if (onClick != null) button.onClick.AddListener(() => onClick());
            return button;
        }

        public static RaidPointer Pointer(Graphic graphic, Action left, Action right = null, Action<bool> hover = null)
        {
            graphic.raycastTarget = true;
            var pointer = graphic.gameObject.GetComponent<RaidPointer>() ?? graphic.gameObject.AddComponent<RaidPointer>();
            pointer.Left = left;
            pointer.Right = right;
            pointer.Hover = hover;
            return pointer;
        }

        public static void Clear(Transform parent)
        {
            for (var i = parent.childCount - 1; i >= 0; i--)
            {
                var old = parent.GetChild(i).gameObject;
                old.SetActive(false);
                UnityEngine.Object.Destroy(old);
            }
        }

        public static bool ShiftHeld
        {
            get
            {
                var keyboard = UnityEngine.InputSystem.Keyboard.current;
                return keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
            }
        }

        public static bool ControlHeld
        {
            get
            {
                var keyboard = UnityEngine.InputSystem.Keyboard.current;
                return keyboard != null && (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed);
            }
        }
    }

    internal class RaidPointer : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public Action Left, Right;
        public Action<bool> Hover;
        private bool _inside;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) Left?.Invoke();
            else if (eventData.button == PointerEventData.InputButton.Right) Right?.Invoke();
        }

        public void OnPointerEnter(PointerEventData eventData) => Set(true);

        public void OnPointerExit(PointerEventData eventData) => Set(false);

        // A hidden object gets no exit event: its tooltip would stay for ever.
        private void OnDisable() => Set(false);

        private void Set(bool inside)
        {
            if (_inside == inside) return;
            _inside = inside;
            Hover?.Invoke(inside);
        }
    }

    /// <summary>
    /// Something picked up with the left button and carried: a hero to another's rank. The click that selects
    /// stays the object's own (<see cref="RaidPointer"/>); a carry starts once the pointer has moved the UI's
    /// own drag distance with the button down.
    /// </summary>
    internal class RaidCarry : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public float Threshold;
        public Func<bool> CanBegin;
        public Action Began;
        /// <summary>Where the pointer is, in screen pixels: while carrying, and where it let go.</summary>
        public Action<Vector2> Moved, Ended;
        private bool _carrying;
        private Vector2 _from;

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || (CanBegin != null && !CanBegin())) return;
            _from = eventData.pressPosition;
            _carrying = true;
            Began?.Invoke();
            Moved?.Invoke(eventData.position);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_carrying) Moved?.Invoke(eventData.position);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!_carrying) return;
            _carrying = false;
            // let go nearly where it was picked up: nothing was carried anywhere
            Ended?.Invoke((eventData.position - _from).magnitude < Threshold ? new Vector2(float.MinValue, float.MinValue) : eventData.position);
        }

        private void OnDisable()
        {
            if (!_carrying) return;
            _carrying = false;
            Ended?.Invoke(new Vector2(float.MinValue, float.MinValue));
        }
    }

    /// <summary>
    /// DD1's look of a picture button under the pointer: the art multiplied by button_highlight
    /// (colours/base.colours.darkest: .hvec4 1.5 1.5 1.3 1.0). A UI picture's own colour cannot pass white, so
    /// the picture is drawn with a material of the UI's shader whose tint is that factor while the pointer
    /// rests on it; pressed, or while it cannot be used, it is not lit.
    /// </summary>
    internal class RaidHighlight : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        // FALLBACK: DD1's stock button_highlight.
        private static readonly Color Stock = new Color(1.5f, 1.5f, 1.3f, 1f);
        private static Material _material;

        /// <summary>The button the picture is; null for a picture that is always lit under the pointer.</summary>
        public Selectable Button;
        private Graphic _graphic;
        private bool _inside, _down, _lit;

        public void OnPointerEnter(PointerEventData eventData)
        {
            _inside = true;
            Apply();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _inside = false;
            Apply();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _down = true;
            Apply();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _down = false;
            Apply();
        }

        // A hidden object gets no exit event: it would come back lit.
        private void OnDisable()
        {
            _inside = _down = false;
            Apply();
        }

        // A button can stop being usable, and become so again, with the pointer on it.
        private void Update()
        {
            if (_inside) Apply();
        }

        private void Apply()
        {
            var lit = _inside && !_down && (Button == null || Button.IsInteractable());
            if (lit == _lit) return;
            if (_graphic == null) _graphic = GetComponent<Graphic>();
            if (_graphic == null) return;
            _lit = lit;
            _graphic.material = lit ? Lit() : null;
        }

        private static Material Lit()
        {
            if (_material != null) return _material;
            _material = new Material(Graphic.defaultGraphicMaterial) { name = "DD2Estate.ButtonHighlight", hideFlags = HideFlags.HideAndDontSave };
            _material.color = Dd1Fonts.Factor("button_highlight", Stock);
            return _material;
        }

        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();

        /// <summary>
        /// A material of the UI's shader that multiplies a picture by one of DD1's colour factors (an .hvec4 of
        /// colours/base.colours.darkest, whose channels may pass 1: inventory_selected 1.4 1.4 1.7).
        /// </summary>
        public static Material Tinted(string factorId, Color stock)
        {
            if (Materials.TryGetValue(factorId, out var material) && material != null) return material;
            material = new Material(Graphic.defaultGraphicMaterial) { name = "DD2Estate." + factorId, hideFlags = HideFlags.HideAndDontSave };
            material.color = Dd1Fonts.Factor(factorId, stock);
            Materials[factorId] = material;
            return material;
        }
    }

    /// <summary>
    /// The dungeon screens' tooltip: a text in a box of DD2's look (<see cref="Dd2TooltipBox"/>: DD2's ground,
    /// font, sizes and colours, read from the game's own tooltip prefab; it was DD1's soft black box until the
    /// batch of 2026-10-06). One per screen: it shows what was asked last and hides when its owner says so.
    /// </summary>
    internal class RaidTooltip
    {
        private readonly Dd2TooltipBox _box;
        private object _owner;

        public RaidTooltip(Transform screen)
        {
            _box = new Dd2TooltipBox(screen);
        }

        /// <summary>
        /// Shows a text whose lines are <paramref name="width"/> wide at most (counted in DD1's font:
        /// <see cref="Dd2TooltipLook.Width"/> makes it DD2's) in a box whose top left corner is at
        /// <paramref name="at"/> of the screen; <paramref name="above"/> puts its bottom edge there instead,
        /// <paramref name="centred"/> its middle. The box is moved back inside the screen when it would leave it.
        /// </summary>
        /// <param name="style">A DD1 text style whose colour the words take (quest_complete_tooltip); null: the tooltip's own. The font is DD2's either way.</param>
        public void Show(object owner, string text, Vector2 at, float width, bool above = false, bool centred = false, string style = null)
        {
            _owner = owner;
            var size = _box.Fill(text, Dd2TooltipLook.Width(width), style != null ? Dd1Fonts.Colour(style, Dd2TooltipLook.TextColour) : (Color?)null);
            var room = _box.Room();
            var corner = new Vector2(centred ? at.x - size.x * 0.5f : at.x, above ? at.y - size.y : at.y);
            // a corner DD1's layout stood beside the thing: DD2's wider box must not come to lie on it
            if (!centred && !above) corner = _box.Beside(owner, corner, size, width + Dd1Box, room);
            else if (!centred) corner = _box.Over(owner, at, size);
            _box.Place(corner, size, room);
        }

        // What DD1's box added to its text's width (twice the old box's room beside the words: half its soft
        // border and tooltip_background_layout's text_offset): callers that stood a tooltip left of its thing
        // counted with it.
        private const float Dd1Box = 27f;

        /// <summary>Hides the tooltip if it is still this owner's.</summary>
        public void Hide(object owner)
        {
            if (!ReferenceEquals(_owner, owner)) return;
            _owner = null;
            _box.Hide();
        }

        public void HideAll()
        {
            _owner = null;
            _box.Hide();
        }

        public bool Shows(object owner) => ReferenceEquals(_owner, owner) && _box.Shown;

        /// <summary>A name over a text, as DD2 heads an item's tooltip: larger, in its heading font and its gold.</summary>
        public static string Titled(string title, string body)
        {
            return string.IsNullOrEmpty(body) ? Dd2TooltipLook.Title(title) : Dd2TooltipLook.Title(title) + "\n" + body;
        }
    }
}
