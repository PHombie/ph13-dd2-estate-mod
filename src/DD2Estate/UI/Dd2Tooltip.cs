using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using Assets.Code.Item;
using Assets.Code.Locale;
using Assets.Code.UI;
using Assets.Code.UI.Canvases;
using Assets.Code.UI.Data;
using Assets.Code.UI.Managers;
using Assets.Code.UI.Tooltips;
using Assets.Code.Utils;
using BepInEx.Configuration;
using DD2Estate.Dd1;
using DD2Estate.Estate;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD2Estate.UI
{
    /// <summary>
    /// What a tooltip of DD2 looks like, read from the game's own tooltip prefab (the one
    /// <see cref="TooltipCanvasUiBhv"/>'s pool copies for every text tooltip) the first time a box is filled:
    /// the ground (ui_tooltip, sliced by its own borders, as its UiImageProperties say), the room the layout
    /// group leaves around the words, the label's font, material, colour and alignment, the two sizes DD2 sets
    /// it in (regular and "large tooltips", the player's option) and the colours DD2's texts name in a tooltip.
    /// The numbers written here are what the game had on 2026-10-05 (v2.04): they stand in only until the prefab
    /// can be read.
    /// </summary>
    [EstateModule]
    internal static class Dd2TooltipLook
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        public static bool Ready { get; private set; }

        // ---- the ground: TooltipCanvasElement/BG, UiImageProperties "tooltip_image" ----
        public static Sprite Ground;
        public static Material GroundMaterial;
        public static Color GroundColour = Color.white;
        public static Image.Type GroundType = Image.Type.Sliced;
        public static bool GroundFillCentre = true;
        public static float GroundPixelsPerUnit = 1f;
        // FALLBACK: a flat box of the art's own black until the sprite can be had.
        public static readonly Color FlatGround = new Color(0.02f, 0.02f, 0.02f, 0.97f);

        // ---- the room around the words: TooltipCanvasElement's VerticalLayoutGroup ----
        public static float PadLeft = 18f, PadRight = 18f, PadTop = 10f, PadBottom = 12f;

        // ---- the words: TooltipCanvasElement/Text ----
        public static TMP_FontAsset Font;
        public static Material FontMaterial;
        public static TMP_SpriteAsset Sprites;
        public static Color TextColour = new Color32(0xAE, 0xAC, 0xA2, 0xFF);
        public static TextAlignmentOptions Alignment = TextAlignmentOptions.Center;
        public static FontStyles Style = FontStyles.Normal;
        public static float LineSpacing, ParagraphSpacing, CharacterSpacing, WordSpacing;
        public static float RegularSize = 24f, LargeSize = 30f;

        // ---- DD2's marks: ItemDescription's title line and the colours its string table names ----
        // FALLBACK values: ItemDescription.TITLE_PREFIX with #{item_nameline}, #{stat_buff}, #{item_discardline}, #{stat_debuff}.
        private static string _titleStart = "<size=120%><font=\"NDDunkelD-Bold SDF\"><color=#B19F61FF>";
        private const string TitleEnd = "</color></font></size>";
        private static string _good = "#B0935EFF", _faint = "#666666FF", _dd2Bad = "#3B6196FF";

        private static ConfigEntry<bool> _negativeRed;
        private static float _nextTry;
        private static readonly Regex ColourMark = new Regex("<color=#([0-9A-Fa-f]{6})([0-9A-Fa-f]{2})?>", RegexOptions.Compiled);
        private static Dictionary<string, string> _recolour;

        private static void Register()
        {
            _negativeRed = Plugin.Settings.Bind("Tooltips", "NegativeRed", true,
                "In the mod's tooltips the colour DD2 gives a harmful number (its blue) is drawn in DD1's red, the mod's colour of harm. False: DD2's own blue.");
            _negativeRed.SettingChanged += (sender, change) => _recolour = null;
        }

        /// <summary>The size DD2 sets a tooltip's words in right now: its regular one, or the large one of the player's "large tooltips" option.</summary>
        public static float FontSize
        {
            get
            {
                try
                {
                    if (SingletonMonoBehaviour<CommonUiBhv>.HasInstance() && !SingletonMonoBehaviour<CommonUiBhv>.Instance.UseLargeTooltips) return RegularSize;
                }
                catch (Exception)
                {
                    // the game is between scenes: the size asked last stands
                }
                return LargeSize;
            }
        }

        /// <summary>
        /// The width DD2's words may take where a DD1 layout gave its small Ubuntu so many pixels: the same
        /// number of words to a line (DD1's tooltip font sets a letter in about 9 pixels of the 1920 wide
        /// screen, DD2's in about half its size: 14.5 at 30, 11.6 at 24), and never less than DD2's own measure
        /// for a describing text (QuirkTooltipBhv's m_maxWidth, 450), so DD1's narrow item boxes do not become
        /// columns of two words a line.
        /// </summary>
        public static float Width(float dd1Width)
        {
            return dd1Width <= 0f ? dd1Width : Mathf.Max(Describing, Mathf.Round(dd1Width * FontSize / Dd1LetterSize));
        }

        // QuirkTooltipBhv.m_maxWidth on DD2's character sheet (read there on 2026-10-05), and the size at which
        // DD2's tooltip font would set a letter as wide as DD1's "tooltip" font (ubuntu_small) does.
        private const float Describing = 450f;
        private const float Dd1LetterSize = 19f;

        /// <summary>A name as DD2 sets the first line of an item's tooltip: larger, in its heading font, in its gold.</summary>
        public static string Title(string title)
        {
            Read();
            return string.IsNullOrEmpty(title) ? "" : _titleStart + title + TitleEnd;
        }

        /// <summary>A heading over a text, as DD2's item tooltips have them. A heading alone is a plain line, as DD2's one-line tooltips are.</summary>
        public static string Compose(string title, string body)
        {
            var hasTitle = !string.IsNullOrEmpty(title);
            var hasBody = !string.IsNullOrEmpty(body);
            if (hasTitle && hasBody) return Title(title) + "\n" + body;
            return hasTitle ? title : hasBody ? body : "";
        }

        /// <summary>
        /// The colour marks the mod's texts carry, on DD2's box: DD1's "notable" becomes the gold DD2 marks a
        /// good thing with, DD1's dark grey of a hint the grey of DD2's hint lines; DD1's "neutral" is the very
        /// colour of DD2's tooltip text and DD1's red stays the colour of harm. DD2's own blue of a harmful
        /// number becomes that red too unless [Tooltips] NegativeRed is off.
        /// </summary>
        public static string Recolour(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf("<color=#", StringComparison.Ordinal) < 0) return text;
            var map = Map();
            return ColourMark.Replace(text, mark => map.TryGetValue(mark.Groups[1].Value.ToUpperInvariant(), out var dd2) ? "<color=" + dd2 + ">" : mark.Value);
        }

        private static Dictionary<string, string> Map()
        {
            if (_recolour != null) return _recolour;
            var map = new Dictionary<string, string>();
            var harmful = "#" + ColorUtility.ToHtmlStringRGB(UiKit.Harmful) + "FF";
            map[ColorUtility.ToHtmlStringRGB(UiKit.Notable)] = _good;
            map[ColorUtility.ToHtmlStringRGB(UiKit.Gold)] = _good;
            map[ColorUtility.ToHtmlStringRGB(UiKit.Neutral)] = "#" + ColorUtility.ToHtmlStringRGBA(TextColour);
            map[ColorUtility.ToHtmlStringRGB(UiKit.Parchment)] = "#" + ColorUtility.ToHtmlStringRGBA(TextColour);
            // DD1's torch_reduce_tip and torch_snuff_tip: 70 70 70, nearly the box's own black.
            map[ColorUtility.ToHtmlStringRGB(Dd1Fonts.Colour("torch_reduce_tip", new Color32(70, 70, 70, 255)))] = _faint;
            map["464646"] = _faint;
            if (_negativeRed == null || _negativeRed.Value) map[_dd2Bad.Substring(1, 6).ToUpperInvariant()] = harmful;
            // The table is kept once both games have been read: before that a colour could still be a stand-in.
            bool dd1;
            try { dd1 = Dd1Install.Found; }
            catch (Exception) { dd1 = false; }
            if (Ready && dd1) _recolour = map;
            return map;
        }

        /// <summary>
        /// Gives a box DD2's tooltip ground (the picture, cut in nine as DD2 cuts it): a tooltip's own, or a
        /// note that stays on the screen and should look like one. False while the look cannot be read: the box
        /// keeps what it had.
        /// </summary>
        public static bool Dress(Image ground)
        {
            if (ground == null || !Read() || Ground == null) return false;
            ground.sprite = Ground;
            ground.material = GroundMaterial;
            ground.color = GroundColour;
            ground.type = GroundType;
            ground.fillCenter = GroundFillCentre;
            ground.pixelsPerUnitMultiplier = GroundPixelsPerUnit;
            return true;
        }

        /// <summary>Reads the look from the game's prefab. False while the game has no tooltip canvas yet.</summary>
        public static bool Read()
        {
            if (Ready) return true;
            if (Time.unscaledTime < _nextTry) return false;
            _nextTry = Time.unscaledTime + 1f;
            try
            {
                if (!SingletonMonoBehaviour<TooltipCanvasUiBhv>.HasInstance()) return false;
                var canvas = SingletonMonoBehaviour<TooltipCanvasUiBhv>.Instance;
                var prefab = Prefab(canvas, "m_tooltipContainer");
                if (prefab == null) return false;

                var element = prefab.GetComponent<TooltipCanvasElementBhv>();
                var label = (element != null ? Field(typeof(TooltipCanvasElementBhv), "m_resizeTextLabel")?.GetValue(element) as TextMeshProUGUI : null) ?? prefab.GetComponentInChildren<TextMeshProUGUI>(true);
                if (label == null || label.font == null) return false;
                Font = label.font;
                FontMaterial = label.fontSharedMaterial;
                Sprites = label.spriteAsset;
                TextColour = label.color;
                Alignment = label.alignment;
                Style = label.fontStyle;
                LineSpacing = label.lineSpacing;
                ParagraphSpacing = label.paragraphSpacing;
                CharacterSpacing = label.characterSpacing;
                WordSpacing = label.wordSpacing;
                if (element != null)
                {
                    if (Field(typeof(TooltipCanvasElementBhv), "m_regularFontSize")?.GetValue(element) is int regular && regular > 0) RegularSize = regular;
                    if (Field(typeof(TooltipCanvasElementBhv), "m_largeFontSize")?.GetValue(element) is int large && large > 0) LargeSize = large;
                }

                var group = prefab.GetComponent<LayoutGroup>();
                if (group != null && group.padding != null)
                {
                    PadLeft = group.padding.left;
                    PadRight = group.padding.right;
                    PadTop = group.padding.top;
                    PadBottom = group.padding.bottom;
                }

                // The ground's picture is not on the prefab's Image: UiImagePropertiesBhv puts it there when a copy starts.
                var dresser = prefab.GetComponentInChildren<UiImagePropertiesBhv>(true);
                var properties = dresser != null ? Field(typeof(UiImagePropertiesBhv), "m_imageProperties")?.GetValue(dresser) as UiImageProperties : null;
                if (properties != null && properties.m_image != null)
                {
                    Ground = properties.m_image;
                    GroundMaterial = properties.m_material;
                    GroundColour = properties.m_color;
                    GroundType = properties.m_type;
                    GroundFillCentre = properties.m_fillCenter;
                    GroundPixelsPerUnit = Mathf.Max(1, properties.m_pixelsPerUnitMultiplier);
                }
                else
                {
                    // a copy that has started carries it
                    foreach (var image in canvas.GetComponentsInChildren<Image>(true))
                    {
                        if (image.sprite == null || image.GetComponent<UiImagePropertiesBhv>() == null) continue;
                        Ground = image.sprite;
                        GroundMaterial = image.material != image.defaultMaterial ? image.material : null;
                        GroundColour = image.color;
                        GroundType = image.type;
                        GroundFillCentre = image.fillCenter;
                        GroundPixelsPerUnit = image.pixelsPerUnitMultiplier;
                        break;
                    }
                }

                // The heading's font is named in the text (<font="NDDunkelD-Bold SDF">): TMP must know it by that
                // name. DD2's skill tooltip sets its title in it.
                var skill = Prefab(canvas, "m_skillTooltipContainer");
                if (skill != null)
                    foreach (var other in skill.GetComponentsInChildren<TextMeshProUGUI>(true))
                        if (other.font != null && other.font != Font) MaterialReferenceManager.AddFontAsset(other.font);
                if (UiKit.Font != null) MaterialReferenceManager.AddFontAsset(UiKit.Font);
                MaterialReferenceManager.AddFontAsset(Font);

                Marks();
                Ready = true;
                _recolour = null;
                Plugin.Log.LogInfo("Tooltips: DD2's look read from its prefab '" + prefab.name + "': ground " + (Ground != null ? Ground.name + " border " + Ground.border : "none")
                                   + ", font " + Font.name + " " + RegularSize + "/" + LargeSize + ", padding " + PadLeft + " " + PadRight + " " + PadTop + " " + PadBottom);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Tooltips: DD2's tooltip prefab could not be read (" + e.GetType().Name + ": " + e.Message + "); the stand-in look stays");
                _nextTry = Time.unscaledTime + 10f;
            }
            return Ready;
        }

        // DD2 writes a colour into a text as #{key}: its string table holds the hex.
        private static void Marks()
        {
            try
            {
                var table = Singleton<Localization>.Instance;
                var prefix = typeof(ItemDescription).GetField("TITLE_PREFIX", Hidden)?.GetRawConstantValue() as string;
                if (!string.IsNullOrEmpty(prefix))
                {
                    var start = table.GetSubstitutedText(prefix);
                    if (!string.IsNullOrEmpty(start) && start.IndexOf("#{", StringComparison.Ordinal) < 0 && start.IndexOf("<color=", StringComparison.Ordinal) >= 0
                        && start.IndexOf("<font=", StringComparison.Ordinal) >= 0 && start.IndexOf("<size=", StringComparison.Ordinal) >= 0) _titleStart = start;
                }
                _good = Hex(table.TryGetString("stat_buff"), _good);
                _faint = Hex(table.TryGetString("item_discardline"), _faint);
                _dd2Bad = Hex(table.TryGetString("stat_debuff"), _dd2Bad);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Tooltips: DD2's text colours could not be asked for (" + e.Message + "); the stand-ins stay");
            }
        }

        private static string Hex(string value, string fallback)
        {
            return !string.IsNullOrEmpty(value) && value[0] == '#' && (value.Length == 7 || value.Length == 9) ? value : fallback;
        }

        private static GameObject Prefab(TooltipCanvasUiBhv canvas, string container)
        {
            var held = Field(typeof(TooltipCanvasUiBhv), container)?.GetValue(canvas) as TooltipCanvasUiBhv.TooltipContainer;
            return held != null && held.m_pool != null ? held.m_pool.prefab : null;
        }

        private static FieldInfo Field(Type type, string name) => type.GetField(name, Hidden);

        /// <summary>What was read, for the test commands.</summary>
        public static object Describe()
        {
            Read();
            return new
            {
                ready = Ready,
                ground = Ground != null ? Ground.name : null,
                groundBorder = Ground != null ? Ground.border.ToString() : null,
                groundType = GroundType.ToString(),
                padding = new[] { PadLeft, PadRight, PadTop, PadBottom },
                font = Font != null ? Font.name : null,
                fontMaterial = FontMaterial != null ? FontMaterial.name : null,
                sprites = Sprites != null ? Sprites.name : null,
                textColour = "#" + ColorUtility.ToHtmlStringRGBA(TextColour),
                alignment = Alignment.ToString(),
                regularSize = RegularSize,
                largeSize = LargeSize,
                size = FontSize,
                titleStart = _titleStart,
                good = _good,
                faint = _faint,
                dd2Bad = _dd2Bad,
                negativeRed = _negativeRed == null || _negativeRed.Value,
                harmful = "#" + ColorUtility.ToHtmlStringRGB(UiKit.Harmful)
            };
        }
    }

    /// <summary>
    /// One tooltip box in DD2's look (<see cref="Dd2TooltipLook"/>): DD2's ground behind one label of DD2's
    /// tooltip text, as large as its words and DD2's padding make it. It appears and goes at once, as DD2's do
    /// (their canvas is switched on and off), and never takes the pointer. The mod's two tooltips
    /// (<see cref="Dd1Tooltip"/> in the hamlet, DD2Estate.Dungeon.RaidTooltip in a dungeon) are this box, placed
    /// where their callers ask.
    /// </summary>
    internal class Dd2TooltipBox
    {
        private static readonly List<WeakReference<Dd2TooltipBox>> All = new List<WeakReference<Dd2TooltipBox>>();
        private static readonly Vector3[] Corners = new Vector3[4];
        private static readonly Vector2 TopLeft = new Vector2(0f, 1f);

        private readonly Image _ground;
        private readonly TextMeshProUGUI _label;
        private bool _dressed;
        private string _text, _shown;
        private float _width, _size;
        private Color _colour;
        private Vector2 _box;

        public RectTransform Root { get; }

        public Dd2TooltipBox(Transform parent, string name = "Tooltip")
        {
            _ground = UiKit.Image(name, parent, null, Dd2TooltipLook.FlatGround);
            Root = (RectTransform)_ground.transform;
            var rect = UiKit.Rect("Text", Root);
            _label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            _label.raycastTarget = false;
            _label.richText = true;
            _label.textWrappingMode = TextWrappingModes.Normal;
            _label.overflowMode = TextOverflowModes.Overflow;
            _label.color = Dd2TooltipLook.TextColour;
            _label.alignment = Dd2TooltipLook.Alignment;
            if (UiKit.Font != null) _label.font = UiKit.Font;
            Root.gameObject.SetActive(false);
            All.Add(new WeakReference<Dd2TooltipBox>(this));
        }

        public bool Shown => Root != null && Root.gameObject.activeSelf;

        /// <summary>
        /// Shows the box with these words (rich text; the mod's colour marks are set on DD2's colours) and says
        /// how large it is. <paramref name="textWidth"/> is the widest a line may be before it wraps (0: never);
        /// <paramref name="colour"/> another colour for unmarked words.
        /// </summary>
        public Vector2 Fill(string text, float textWidth, Color? colour = null)
        {
            Dress();
            Root.gameObject.SetActive(true);
            var size = Dd2TooltipLook.FontSize;
            var tint = colour ?? Dd2TooltipLook.TextColour;
            text = text ?? "";
            if (text == _text && Mathf.Approximately(textWidth, _width) && Mathf.Approximately(size, _size) && tint == _colour) return _box;
            _text = text;
            _width = textWidth;
            _size = size;
            _colour = tint;
            _shown = Dd2TooltipLook.Recolour(text);
            _label.fontSize = size;
            _label.color = tint;
            _label.text = _shown;
            var limit = textWidth > 0f ? textWidth : 100000f;
            var wish = _label.GetPreferredValues(_shown, limit, 0f);
            var words = new Vector2(Mathf.Min(limit, Mathf.Ceil(wish.x) + 1f), Mathf.Ceil(wish.y));
            ((RectTransform)_label.transform).PlaceTopLeft(new Vector2(Dd2TooltipLook.PadLeft, Dd2TooltipLook.PadTop), TopLeft, words);
            _box = new Vector2(words.x + Dd2TooltipLook.PadLeft + Dd2TooltipLook.PadRight, words.y + Dd2TooltipLook.PadTop + Dd2TooltipLook.PadBottom);
            return _box;
        }

        /// <summary>
        /// The room the box may stand in, as its parent counts (pixels from the parent's upper left, y down): the
        /// screen, and no further right or down than a limit of the frame it belongs to (0: no such limit).
        /// </summary>
        public Rect Room(float rightEdge = 0f, float bottomEdge = 0f)
        {
            // FALLBACK: a parent that is the mod's whole 1920x1080 canvas, while the box hangs on no canvas yet.
            var room = new Rect(0f, 0f, 1920f, 1080f);
            var parent = Root.parent as RectTransform;
            var canvas = Root.GetComponentInParent<Canvas>(true);
            if (parent != null && canvas != null)
            {
                ((RectTransform)canvas.rootCanvas.transform).GetWorldCorners(Corners);
                room = InParent(parent);
            }
            if (rightEdge > 0f)
            {
                room.xMin = Mathf.Max(room.xMin, 0f);
                room.xMax = Mathf.Min(room.xMax, rightEdge);
            }
            if (bottomEdge > 0f)
            {
                room.yMin = Mathf.Max(room.yMin, 0f);
                room.yMax = Mathf.Min(room.yMax, bottomEdge);
            }
            return room;
        }

        /// <summary>
        /// Puts the box's upper left corner at a place of its parent (pixels from the parent's upper left, y
        /// down) and, as DD2 does with a tooltip that would leave the screen (TooltipUiBhv.UpdateDisplayPosition:
        /// whatever hangs over an edge is taken back, with no margin), moves it back inside the room. A box
        /// larger than the room keeps its upper left corner in view.
        /// </summary>
        public void Place(Vector2 topLeft, Vector2 size, Rect room)
        {
            var x = topLeft.x;
            var y = topLeft.y;
            if (x + size.x > room.xMax) x = room.xMax - size.x;
            if (x < room.xMin) x = room.xMin;
            if (y + size.y > room.yMax) y = room.yMax - size.y;
            if (y < room.yMin) y = room.yMin;
            Root.PlaceTopLeft(new Vector2(Mathf.Round(x), Mathf.Round(y)), TopLeft, size);
            Root.SetAsLastSibling();
        }

        public void Place(Vector2 topLeft, Vector2 size) => Place(topLeft, size, Room());

        /// <summary>
        /// Where a box of DD2's size goes when a DD1 layout gave the upper left corner of DD1's box
        /// (<paramref name="dd1Box"/> wide at most) beside the thing the tooltip speaks of. DD2's box is wider:
        /// one that DD1 stood left of its thing keeps the edge that faces the thing, and one that has no room
        /// right of its thing goes to the thing's other side, as far from it. That can be told only when the
        /// tooltip's owner is the thing itself (something drawn, with a place on the screen) or holds it (a
        /// grid whose slot lies next to the corner); otherwise the corner stays where it was asked. Beside a
        /// thing that is known the box stands as DD2's do (TooltipUiBhv: pivot y 0.5 at the thing's 0.5): level
        /// with the thing's middle.
        /// </summary>
        public Vector2 Beside(object owner, Vector2 topLeft, Vector2 size, float dd1Box, Rect room)
        {
            if (!Thing(owner, topLeft, dd1Box, out var thing)) return topLeft;
            var y = Mathf.Round(thing.center.y - size.y * 0.5f);
            // left of the thing: the box ends where DD1's did, and before the thing (DD1's widest box may reach
            // a little over the thing's edge: its words seldom filled it)
            if (topLeft.x < thing.xMax - 1f) return new Vector2(Mathf.Min(topLeft.x + dd1Box, thing.xMin - Clear) - size.x, y);
            if (topLeft.x + size.x > room.xMax)
            {
                var other = thing.xMin - (topLeft.x - thing.xMax) - size.x;
                if (other >= room.xMin) return new Vector2(other, y);
            }
            return new Vector2(topLeft.x, y);
        }

        /// <summary>
        /// The upper left corner of a box that was asked for over a thing, its lower left corner given: as DD2
        /// stands a tooltip over a thing (pivot 0.5, 0 at the thing's 0.5, 1), its middle over the thing's
        /// middle. That can be told only when the tooltip's owner is the thing and the corner lies at its top;
        /// otherwise the box grows up and to the right of the corner, as it was asked.
        /// </summary>
        public Vector2 Over(object owner, Vector2 bottomLeft, Vector2 size)
        {
            var corner = new Vector2(bottomLeft.x, bottomLeft.y - size.y);
            if (!Whole(owner, out _, out var thing) || thing.width <= 0f || thing.height <= 0f) return corner;
            if (bottomLeft.y > thing.yMin + Edge || bottomLeft.y < thing.yMin - Level || bottomLeft.x < thing.xMin - Edge || bottomLeft.x > thing.xMax) return corner;
            return new Vector2(Mathf.Round(thing.center.x - size.x * 0.5f), corner.y);
        }

        // How far from its thing a layout stands a tooltip at most, and how far above the thing's top the
        // box may begin: further off, what was found is not the thing.
        private const float Near = 240f, Level = 48f, Edge = 12f;
        // How far DD1's widest box may reach over the edge of the thing it stands left of and still be "left of
        // it" (the dungeon's map buttons: 36), and the room DD2's box leaves before the thing then.
        private const float Lap = 48f, Clear = 6f;

        // Where a tooltip's owner lies, as the box's parent counts, when it is something with a place on the screen.
        private bool Whole(object owner, out RectTransform rect, out Rect whole)
        {
            whole = default;
            rect = null;
            if (owner is Component component && component != null) rect = component.transform as RectTransform;
            else if (owner is GameObject thingObject && thingObject != null) rect = thingObject.transform as RectTransform;
            var parent = Root.parent as RectTransform;
            if (rect == null || parent == null || !rect.gameObject.activeInHierarchy) return false;
            rect.GetWorldCorners(Corners);
            whole = InParent(parent);
            return true;
        }

        // The thing DD1's box was stood beside: the tooltip's owner, or the part of it (a child: a grid's slot)
        // that lies next to the corner when the owner is the whole the corner lies in.
        private bool Thing(object owner, Vector2 corner, float dd1Box, out Rect thing)
        {
            thing = default;
            if (!Whole(owner, out var rect, out var whole)) return false;
            var parent = (RectTransform)Root.parent;
            // a grid of slots is never the thing itself, though the corner beside its last slot lies beside it too
            var grid = Grid(rect);
            var beside = Gap(whole, corner, dd1Box, true, true, Lap) >= 0f;
            if (beside && !grid)
            {
                thing = whole;
                return true;
            }
            // an owner that lies elsewhere is not what the tooltip speaks of (a grid with no size of its own may still hold it)
            if (!grid && whole.width > 0f && whole.height > 0f && !whole.Contains(corner)) return false;
            // DD1 hangs a tooltip right of its thing: a part on that side is taken before one on the other
            for (var side = 0; side < 2; side++)
            {
                var nearest = float.MaxValue;
                for (var i = 0; i < rect.childCount; i++)
                {
                    if (!(rect.GetChild(i) is RectTransform part) || !part.gameObject.activeInHierarchy) continue;
                    part.GetWorldCorners(Corners);
                    var place = InParent(parent);
                    var gap = Gap(place, corner, dd1Box, side == 0, side == 1);
                    if (gap < 0f || gap >= nearest) continue;
                    nearest = gap;
                    thing = place;
                }
                if (nearest < float.MaxValue) return true;
            }
            if (!beside) return false;
            thing = whole;
            return true;
        }

        // Whether something is a grid: several parts of one size (the bag's slots, a shop's shelves).
        private static bool Grid(RectTransform rect)
        {
            var same = 0;
            var size = Vector2.zero;
            for (var i = 0; i < rect.childCount; i++)
            {
                if (!(rect.GetChild(i) is RectTransform part) || !part.gameObject.activeSelf) continue;
                var own = part.rect.size;
                if (own.x <= 0f || own.y <= 0f) continue;
                if (same == 0) size = own;
                if (Mathf.Abs(own.x - size.x) < 1f && Mathf.Abs(own.y - size.y) < 1f) same++;
            }
            return same >= 4;
        }

        // How far DD1's box at a corner stands from a rect it is beside (right of it or left of it, near, and
        // level with it); below zero when it is not beside it.
        private static float Gap(Rect thing, Vector2 corner, float dd1Box, bool right = true, bool left = true, float lap = 1f)
        {
            if (thing.width <= 0f || thing.height <= 0f || corner.y < thing.yMin - Level || corner.y > thing.yMax) return -1f;
            var toRight = corner.x - thing.xMax;
            if (right && toRight >= -1f && toRight <= Near) return Mathf.Max(0f, toRight);
            var toLeft = thing.xMin - (corner.x + dd1Box);
            return left && corner.x < thing.xMin && toLeft >= -lap && toLeft <= Near ? Mathf.Max(0f, toLeft) : -1f;
        }

        // What Corners hold (a rect's corners in the world), in the parent's pixels from its upper left, y down.
        private static Rect InParent(RectTransform parent)
        {
            Vector2 low = parent.InverseTransformPoint(Corners[0]);
            Vector2 high = parent.InverseTransformPoint(Corners[2]);
            var own = parent.rect;
            return Rect.MinMaxRect(Mathf.Min(low.x, high.x) - own.xMin, own.yMax - Mathf.Max(low.y, high.y), Mathf.Max(low.x, high.x) - own.xMin, own.yMax - Mathf.Min(low.y, high.y));
        }

        public void Hide()
        {
            if (Root != null) Root.gameObject.SetActive(false);
        }

        // The ground and the label take DD2's look as soon as it can be read; a box made before that is a flat
        // dark one in the mod's font until then.
        private void Dress()
        {
            if (_dressed || !Dd2TooltipLook.Read()) return;
            _dressed = true;
            _text = null;
            Dd2TooltipLook.Dress(_ground);
            _label.font = Dd2TooltipLook.Font;
            if (Dd2TooltipLook.FontMaterial != null) _label.fontSharedMaterial = Dd2TooltipLook.FontMaterial;
            if (Dd2TooltipLook.Sprites != null) _label.spriteAsset = Dd2TooltipLook.Sprites;
            _label.alignment = Dd2TooltipLook.Alignment;
            _label.fontStyle = Dd2TooltipLook.Style;
            _label.lineSpacing = Dd2TooltipLook.LineSpacing;
            _label.paragraphSpacing = Dd2TooltipLook.ParagraphSpacing;
            _label.characterSpacing = Dd2TooltipLook.CharacterSpacing;
            _label.wordSpacing = Dd2TooltipLook.WordSpacing;
            _label.extraPadding = false;
        }

        /// <summary>Every box that still exists, for the test commands: where it stands on the screen (canvas pixels from the upper left) and what it says.</summary>
        public static List<object> Snapshot(bool shownOnly)
        {
            var list = new List<object>();
            for (var i = All.Count - 1; i >= 0; i--)
            {
                if (!All[i].TryGetTarget(out var box) || box.Root == null)
                {
                    All.RemoveAt(i);
                    continue;
                }
                var shown = box.Root.gameObject.activeInHierarchy;
                if (shownOnly && !shown) continue;
                object rect = null;
                bool? inside = null;
                var canvas = box.Root.GetComponentInParent<Canvas>(true);
                if (canvas != null)
                {
                    var screen = (RectTransform)canvas.rootCanvas.transform;
                    box.Root.GetWorldCorners(Corners);
                    Vector2 low = screen.InverseTransformPoint(Corners[0]);
                    Vector2 high = screen.InverseTransformPoint(Corners[2]);
                    var room = screen.rect;
                    rect = new { x = Mathf.Round(low.x - room.xMin), y = Mathf.Round(room.yMax - high.y), width = Mathf.Round(high.x - low.x), height = Mathf.Round(high.y - low.y) };
                    inside = low.x >= room.xMin - 0.5f && low.y >= room.yMin - 0.5f && high.x <= room.xMax + 0.5f && high.y <= room.yMax + 0.5f;
                }
                list.Add(new
                {
                    path = Dev.AgentBridge.PathOf(box.Root),
                    shown,
                    dressed = box._dressed,
                    rect,
                    inside,
                    font = box._label.font != null ? box._label.font.name : null,
                    size = box._label.fontSize,
                    lines = box._label.textInfo != null ? box._label.textInfo.lineCount : 0,
                    ground = box._ground.sprite != null ? box._ground.sprite.name : null,
                    text = box._shown
                });
            }
            return list;
        }
    }
}
