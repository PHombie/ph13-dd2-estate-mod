using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using DD2Estate.Dd1;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DD2Estate.UI
{
    /// <summary>
    /// What the hamlet's windows share on top of <see cref="UiKit"/>: numbers out of DD1's layout files, DD1 art
    /// that is quietly absent when the install has no such file, picture buttons as DD1 draws them, a list that
    /// scrolls beside DD1's scroll bar, labels in a DD1 text style placed the way
    /// DD1's layout files count (pixels from the parent's top left corner, y down), and DD1's tooltip box.
    /// Nothing here is ever cut off with an ellipsis: one line shrinks to its box, a paragraph wraps and shrinks.
    /// </summary>
    internal static class Dd1Ui
    {
        public static readonly Vector2 TopLeft = new Vector2(0f, 1f);
        public static readonly Vector2 TopCentre = new Vector2(0.5f, 1f);
        public static readonly Vector2 Middle = new Vector2(0.5f, 0.5f);

        private static readonly Dictionary<string, Sprite> Slices = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, Sprite> Tones = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, Vector2> ToneValues = new Dictionary<string, Vector2>();    // darkness, saturation by colour id

        // ---- DD1's layout files --------------------------------------------------------------------------

        /// <summary>A pair of numbers of a layout block; <paramref name="x"/>, <paramref name="y"/> (DD1's stock values) if it cannot be read.</summary>
        public static Vector2 Offset(DarkestFile layout, string block, string key, float x, float y)
        {
            var entry = layout?.Find(block);
            return entry != null && entry.Values(key).Count >= 2 ? entry.Vector2(key) : new Vector2(x, y);
        }

        public static float Number(DarkestFile layout, string block, string key, float fallback)
        {
            var entry = layout?.Find(block);
            return entry != null && entry.Has(key) ? entry.Float(key, 0, fallback) : fallback;
        }

        /// <summary>A DD1 layout file; null (and nothing in the log) without a DD1 install.</summary>
        public static DarkestFile Layout(string file) => Dd1Install.Found ? DarkestFile.Load(file) : null;

        // ---- art -----------------------------------------------------------------------------------------

        /// <summary>A DD1 picture; null (and no warning in the log) when the install has no such file.</summary>
        public static Sprite Sprite(string dd1File)
        {
            // "sheet.png#name": one picture out of a sprite sheet
            var hash = dd1File.IndexOf('#');
            return Dd1Install.Found && Dd1Install.Exists(hash > 0 ? dd1File.Substring(0, hash) : dd1File) ? Dd1Install.Sprite(dd1File) : null;
        }

        /// <summary>
        /// A DD1 image at its own size (or <paramref name="size"/>), its corner at <paramref name="topLeft"/>.
        /// If the file is missing the image is invisible, or a flat <paramref name="fallback"/> box where
        /// something must stay visible or clickable.
        /// </summary>
        public static Image Art(string name, Transform parent, string dd1File, Vector2 topLeft, Vector2? size = null, Color? fallback = null, bool raycast = false)
        {
            var sprite = Sprite(dd1File);
            var image = UiKit.Image(name, parent, sprite, sprite != null ? Color.white : fallback ?? Color.clear, raycast);
            ((RectTransform)image.transform).PlaceTopLeft(topLeft, TopLeft, size ?? (sprite != null ? sprite.rect.size : Vector2.zero));
            return image;
        }

        /// <summary>
        /// A DD1 picture cut in nine: the corners keep their size, the rest stretches. <paramref name="border"/>
        /// is the share of the picture's width kept at every edge (tooltip.layout.darkest: border_texture_threshold).
        /// </summary>
        public static Sprite Sliced(string dd1File, float border)
        {
            if (Slices.TryGetValue(dd1File, out var cached) && cached != null) return cached;
            var texture = Dd1Install.Found && Dd1Install.Exists(dd1File) ? Dd1Install.Texture(dd1File) : null;
            if (texture == null) return null;
            var edge = Mathf.Round(texture.width * border);
            var sprite = UnityEngine.Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), Middle, 100f, 0, SpriteMeshType.FullRect, new Vector4(edge, edge, edge, edge));
            sprite.name = dd1File + " (sliced)";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            Slices[dd1File] = sprite;
            return sprite;
        }

        /// <summary>
        /// A DD1 picture in one of DD1's dimmed colours. colours/base.colours.darkest gives such a colour two
        /// numbers instead of an rgba: `.darkness`, read here as the share of its light a picture keeps (the
        /// roster's unselectable portraits have 0.4 and are dark, the navigation's resting buttons 0.8), and
        /// `.saturation`, the share of its colour (0.2: all but grey). A tint cannot take colour out of a
        /// picture, so the picture is made anew, once. <paramref name="darkness"/> and
        /// <paramref name="saturation"/> are DD1's stock values of <paramref name="colourId"/>, used when the
        /// table cannot be read. Null when the install has no such file.
        /// </summary>
        public static Sprite Toned(string dd1File, string colourId, float darkness, float saturation)
        {
            var key = dd1File + "@" + colourId;
            if (Tones.TryGetValue(key, out var cached) && cached != null) return cached;
            if (!Dd1Install.Found || !Dd1Install.Exists(dd1File)) return null;
            Tone(colourId, ref darkness, ref saturation);
            var texture = Dd1Install.LoadTexture(dd1File, true, false);
            if (texture == null) return null;
            var pixels = texture.GetPixels32();
            for (var i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];
                var grey = 0.299f * p.r + 0.587f * p.g + 0.114f * p.b;
                pixels[i] = new Color32(Shade(grey, p.r, saturation, darkness), Shade(grey, p.g, saturation, darkness), Shade(grey, p.b, saturation, darkness), p.a);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            var sprite = UnityEngine.Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), Middle, 100f, 0, SpriteMeshType.FullRect);
            sprite.name = dd1File + " (" + colourId + ")";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            Tones[key] = sprite;
            return sprite;
        }

        private static byte Shade(float grey, byte channel, float saturation, float darkness)
        {
            return (byte)Mathf.Clamp(Mathf.RoundToInt((grey + (channel - grey) * saturation) * darkness), 0, 255);
        }

        // `colour: .id "town_navigation_button_unselected" .darkness 0.8 .saturation 0.2`
        private static void Tone(string colourId, ref float darkness, ref float saturation)
        {
            if (ToneValues.TryGetValue(colourId, out var known))
            {
                darkness = known.x;
                saturation = known.y;
                return;
            }
            Read(colourId, ref darkness, ref saturation);
            ToneValues[colourId] = new Vector2(darkness, saturation);
        }

        private static void Read(string colourId, ref float darkness, ref float saturation)
        {
            try
            {
                var table = Dd1Install.ReadText("colours/base.colours.darkest");
                if (table == null) return;
                foreach (var line in table.Split('\n'))
                {
                    if (line.IndexOf("\"" + colourId + "\"", StringComparison.Ordinal) < 0) continue;
                    var dark = Regex.Match(line, "\\.darkness\\s+([0-9.]+)");
                    var colour = Regex.Match(line, "\\.saturation\\s+([0-9.]+)");
                    if (dark.Success && float.TryParse(dark.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) darkness = d;
                    if (colour.Success && float.TryParse(colour.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var s)) saturation = s;
                    return;
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("DD1 colour " + colourId + " could not be read, its stock values stand: " + e.Message); }
        }

        /// <summary>
        /// A DD1 picture that is a button, as DD1 draws one: at rest the art as it is painted, under the
        /// pointer the art multiplied by button_highlight (colours/base.colours.darkest: brighter than the
        /// art), with DD1's sound of the pointer coming onto a button; pressed it darkens, and it dims while it
        /// cannot be used. <paramref name="highlight"/> false: a button DD1 gives a glow of its own for the
        /// pointer, which is not brightened as well.
        /// </summary>
        public static Button ArtButton(string name, Transform parent, string dd1File, Vector2 topLeft, Vector2 size, Action onClick, string fallbackText = null, bool highlight = true)
        {
            var sprite = Sprite(dd1File);
            var button = UiKit.Button(name, parent, sprite != null ? null : fallbackText, 22f, onClick, sprite);
            ((RectTransform)button.transform).PlaceTopLeft(topLeft, TopLeft, sprite != null ? sprite.rect.size : size);
            Painted(button, highlight);
            return button;
        }

        /// <summary>Makes a button of a DD1 picture look and sound as <see cref="ArtButton"/>'s do.</summary>
        public static Button Painted(Button button, bool highlight = true)
        {
            var colours = button.colors;
            colours.normalColor = Color.white;
            colours.highlightedColor = Color.white;
            colours.selectedColor = Color.white;
            colours.pressedColor = new Color(0.7f, 0.6f, 0.5f);
            colours.disabledColor = new Color(0.4f, 0.4f, 0.4f, 0.7f);
            button.colors = colours;
            if (highlight && button.GetComponent<Dd1Highlight>() == null) button.gameObject.AddComponent<Dd1Highlight>().Button = button;
            UiKit.Hover(button.gameObject, inside =>
            {
                // DD1's exe names /ui/town/button_mouse_over beside its buttons (which of them play it is in no file)
                if (inside && button != null && button.IsInteractable()) DD2Estate.Dd2.EstateAudio.Ui("ui/town/button_mouse_over");
            });
            return button;
        }

        /// <summary>
        /// A window on a list that scrolls by the wheel, with DD1's scroll bar (<see cref="Dd1ScrollBar"/>)
        /// <paramref name="barOffset"/> to its right. Returns the list: rows are placed from its top, and its
        /// height is the caller's to set. <paramref name="always"/>: the bar stands whether or not the list
        /// outgrows the window, as DD1's does (its Graveyard without a grave has rail, arrows and pip); false,
        /// it comes and goes with the need for it. <paramref name="arrows"/> false: a screen with arrows of its
        /// own art, which it hands to the bar (<see cref="Dd1ScrollBar.Follow"/>).
        /// </summary>
        public static RectTransform ScrollList(string name, Transform parent, Vector2 topLeft, Vector2 size, float barOffset, out ScrollRect scroll, bool always = true, bool arrows = true)
        {
            var area = UiKit.Rect(name, parent).PlaceTopLeft(topLeft, TopLeft, size);
            var catcher = area.gameObject.AddComponent<Image>();     // the wheel needs something to land on
            catcher.color = new Color(0f, 0f, 0f, 0.02f);
            area.gameObject.AddComponent<RectMask2D>();
            var list = UiKit.Rect("List", area);
            list.anchorMin = new Vector2(0f, 1f);
            list.anchorMax = new Vector2(1f, 1f);
            list.pivot = TopCentre;
            list.anchoredPosition = Vector2.zero;
            list.sizeDelta = Vector2.zero;
            scroll = area.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = area;
            scroll.content = list;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 45f;
            Dd1ScrollBar.Build(name + ".Bar", parent, scroll, topLeft + new Vector2(size.x + barOffset, 0f), size.y, always, arrows);
            return list;
        }

        // ---- text ----------------------------------------------------------------------------------------

        /// <summary>
        /// One line in a DD1 text style, drawn 1:1 with its line cell's corner at <paramref name="topLeft"/>.
        /// Text wider than <paramref name="size"/> shrinks; it is never cut.
        /// </summary>
        public static TextMeshProUGUI Line(string name, Transform parent, string style, Vector2 topLeft, Vector2 size, TextAlignmentOptions align = TextAlignmentOptions.TopLeft, Color? colour = null)
        {
            var label = UiKit.Text(name, parent, "", style, colour, align);
            ((RectTransform)label.transform).PlaceTopLeft(Corner(topLeft, size, align), TopLeft, size);
            NoCut(label, label.fontSize * 0.5f);
            return label;
        }

        /// <summary>A paragraph in a DD1 text style: it wraps at the box's width and shrinks to stay inside its height.</summary>
        public static TextMeshProUGUI Block(string name, Transform parent, string style, Vector2 topLeft, Vector2 size, TextAlignmentOptions align = TextAlignmentOptions.TopLeft, Color? colour = null, float smallest = 0f)
        {
            var label = UiKit.Text(name, parent, "", style, colour, align);
            ((RectTransform)label.transform).PlaceTopLeft(Corner(topLeft, size, align), TopLeft, size);
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Overflow;
            label.enableAutoSizing = true;
            label.fontSizeMax = label.fontSize;
            label.fontSizeMin = smallest > 0f ? smallest : Mathf.Max(11f, label.fontSize * 0.55f);
            label.richText = true;
            return label;
        }

        /// <summary>Makes a one-line label shrink to its box instead of ending in an ellipsis.</summary>
        public static void NoCut(TextMeshProUGUI label, float smallest)
        {
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.enableAutoSizing = true;
            label.fontSizeMax = label.fontSize;
            label.fontSizeMin = Mathf.Min(label.fontSize, Mathf.Max(8f, smallest));
            label.richText = true;
        }

        // DD1 gives a centred text its middle and a right-aligned one its right end.
        private static Vector2 Corner(Vector2 at, Vector2 size, TextAlignmentOptions align)
        {
            switch (align)
            {
                case TextAlignmentOptions.Top:
                case TextAlignmentOptions.Center:
                case TextAlignmentOptions.Bottom:
                    return new Vector2(at.x - size.x * 0.5f, at.y);
                case TextAlignmentOptions.TopRight:
                case TextAlignmentOptions.Right:
                case TextAlignmentOptions.BottomRight:
                    return new Vector2(at.x - size.x, at.y);
                default:
                    return at;
            }
        }

        /// <summary>"#c8b46e": a DD1 colour (colours/base.colours.darkest) for a rich text tag.</summary>
        public static string Hex(string dd1Colour)
        {
            return "#" + ColorUtility.ToHtmlStringRGB(Dd1Fonts.Colour(dd1Colour, UiKit.Parchment));
        }

        /// <summary>Text in a DD1 colour inside a rich text label.</summary>
        public static string Tint(string text, string dd1Colour)
        {
            return "<color=" + Hex(dd1Colour) + ">" + text + "</color>";
        }
    }

    /// <summary>
    /// DD1's scroll bar, one widget of six pictures (shared/widgets: scrollbarmid.png the rail, scrollbartop.png
    /// and scrollbarbottom.png its end caps, scrollpip.png, scrollbar_uparrow.png and scrollbar_downarrow.png;
    /// DD1's exe loads the six together). No layout file places its parts; they stand as measured on DD1's own
    /// screens (1920x1080 frames of the Activity Log's two lists, the Glossary, the Graveyard and the Ancestor's
    /// Memoirs, where they lie alike to the pixel):
    ///
    ///   the rail      as tall as the list's window, a layout's scrollbar_offset right of it
    ///   the caps      past the rail's two ends (the bottom one begins where the window ends)
    ///   the pip       its middle on the rail's top end while the list is at its top (and, by the same rule
    ///                 turned round, on the bottom end at the list's foot: no frame shows a list scrolled down)
    ///   the arrows    centred on the rail; the upper one's picture begins 32 px above the window, the lower
    ///                 one's 5 px under it
    ///
    /// The bar is there whether or not the list outgrows its window (the Graveyard's frame has no grave and the
    /// whole bar) unless it was asked to come and go. A click on an arrow moves the list by <see cref="Step"/>
    /// with DD1's sound of it (/ui/shared/button_scroll_up, _down); the rail takes the pointer: where it is
    /// pressed or dragged, the pip goes.
    /// </summary>
    internal class Dd1ScrollBar : MonoBehaviour
    {
        private const string Dir = "shared/widgets/";
        public const float RailWidth = 21f;                                      // scrollbarmid.png
        private static readonly Vector2 CapSize = new Vector2(21f, 12f);        // scrollbartop.png, scrollbarbottom.png
        private static readonly Vector2 PipSize = new Vector2(24f, 24f);        // scrollpip.png
        public static readonly Vector2 ArrowSize = new Vector2(41f, 25f);       // scrollbar_uparrow.png, scrollbar_downarrow.png
        private const float ArrowAbove = 32f, ArrowBelow = 5f;                  // measured on DD1's frames
        private const float PipShift = 0.5f;                                    // DD1 draws on whole pixels: the 24 px pip 1 px left of the 21 px rail

        private ScrollRect _scroll;
        private RectTransform _pip, _up, _down;
        private GameObject[] _arrows = new GameObject[0];
        private bool _shown = true;

        /// <summary>The rail: caps and pip hang on it, so a screen may move or resize it.</summary>
        public RectTransform Rail { get; private set; }

        /// <summary>The bar stands whether or not there is anything to scroll.</summary>
        public bool Always = true;

        /// <summary>
        /// How far a click on an arrow moves the list (pixels). The mod's own: DD1's files give its arrows no
        /// step. Three turns of the wheel unless a screen sets it.
        /// </summary>
        public float Step = 135f;

        /// <summary>The bar of a list made by <see cref="Dd1Ui.ScrollList"/>.</summary>
        public static Dd1ScrollBar Of(ScrollRect scroll) => scroll != null ? scroll.GetComponent<Dd1ScrollBar>() : null;

        /// <summary>Whether the list is longer than its window.</summary>
        public bool Scrolls => Room > 0.5f;

        /// <summary>Whether rail, pip and arrows are on show.</summary>
        public bool Shown => _shown;

        /// <param name="railTopLeft">The rail's corner in the parent's pixels (top-left origin, y down); it is <paramref name="height"/> tall.</param>
        public static Dd1ScrollBar Build(string name, Transform parent, ScrollRect scroll, Vector2 railTopLeft, float height, bool always, bool arrows)
        {
            // on the list's window, which stays on while the rail may be switched off
            var bar = scroll.gameObject.AddComponent<Dd1ScrollBar>();
            bar._scroll = scroll;
            bar.Always = always;

            var rail = Dd1Ui.Art(name, parent, Dir + "scrollbarmid.png", railTopLeft, new Vector2(RailWidth, height), new Color(0.12f, 0.11f, 0.1f, 0.9f), true);
            bar.Rail = (RectTransform)rail.transform;
            bar.Rail.sizeDelta = new Vector2(RailWidth, height);
            rail.gameObject.AddComponent<Dd1ScrollRail>().Bar = bar;
            Cap(bar.Rail, "CapTop", "scrollbartop.png", true);
            Cap(bar.Rail, "CapBottom", "scrollbarbottom.png", false);
            var pip = UiKit.Image("Pip", bar.Rail, Dd1Ui.Sprite(Dir + "scrollpip.png"));
            if (pip.sprite == null) pip.color = UiKit.Blood;
            bar._pip = ((RectTransform)pip.transform).Place(new Vector2(0.5f, 1f), Dd1Ui.Middle, new Vector2(PipShift, 0f), PipSize);

            if (arrows)
            {
                bar._up = (RectTransform)bar.Arrow(name + ".Up", parent, Dir + "scrollbar_uparrow.png", Vector2.zero, -1, "^").transform;
                bar._down = (RectTransform)bar.Arrow(name + ".Down", parent, Dir + "scrollbar_downarrow.png", Vector2.zero, 1, "v").transform;
                bar.Follow(bar._up.gameObject, bar._down.gameObject);
                bar.Place(railTopLeft, height);
            }
            bar.Seat();
            return bar;
        }

        /// <summary>
        /// Moves the rail, and DD1's two arrows with it: a screen whose rail is not as tall as its list's window
        /// (the Stage Coach: scroll_bar_y_size_offset). In the pixels of the bar's parent, top-left origin, y down.
        /// </summary>
        public void Place(Vector2 railTopLeft, float height)
        {
            Rail.PlaceTopLeft(railTopLeft, Dd1Ui.TopLeft, new Vector2(RailWidth, height));
            var left = railTopLeft.x + (RailWidth - ArrowSize.x) * 0.5f;
            if (_up != null) _up.PlaceTopLeft(new Vector2(left, railTopLeft.y - ArrowAbove), Dd1Ui.TopLeft, _up.sizeDelta);
            if (_down != null) _down.PlaceTopLeft(new Vector2(left, railTopLeft.y + height + ArrowBelow), Dd1Ui.TopLeft, _down.sizeDelta);
        }

        private static void Cap(RectTransform rail, string name, string art, bool top)
        {
            var sprite = Dd1Ui.Sprite(Dir + art);
            if (sprite == null) return;
            var cap = UiKit.Image(name, rail, sprite);
            ((RectTransform)cap.transform).Place(new Vector2(0.5f, top ? 1f : 0f), new Vector2(0.5f, top ? 0f : 1f), Vector2.zero, CapSize);
        }

        /// <summary>
        /// An arrow of the bar: a picture button that moves the list one <see cref="Step"/> up (-1) or down (1)
        /// with DD1's sound. For a screen whose arrows have art and places of their own; they are shown and
        /// hidden with the rail once handed to <see cref="Follow"/>.
        /// </summary>
        public GameObject Arrow(string name, Transform parent, string dd1File, Vector2 topLeft, int direction, string fallbackText = null)
        {
            // not UiKit.Button's click: an arrow has DD1's own sound
            var sprite = Dd1Ui.Sprite(dd1File);
            var image = UiKit.Image(name, parent, sprite, sprite != null ? Color.white : new Color(0f, 0f, 0f, 0.55f), true);
            ((RectTransform)image.transform).PlaceTopLeft(topLeft, Dd1Ui.TopLeft, sprite != null ? sprite.rect.size : ArrowSize);
            if (sprite == null && !string.IsNullOrEmpty(fallbackText)) ((RectTransform)UiKit.Text("Label", image.transform, fallbackText, 22f).transform).Stretch();
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => Nudge(direction));
            Dd1Ui.Painted(button);
            return image.gameObject;
        }

        /// <summary>These come and go with the rail (a screen's own arrows).</summary>
        public void Follow(params GameObject[] arrows)
        {
            _arrows = arrows ?? new GameObject[0];
            Seat();
        }

        /// <summary>What a click on an arrow does: the list moves by <see cref="Step"/>, down for 1 and up for -1. False when there is nothing to scroll.</summary>
        public bool Nudge(int direction)
        {
            DD2Estate.Dd2.EstateAudio.Ui(direction < 0 ? "ui/shared/button_scroll_up" : "ui/shared/button_scroll_down");
            return MoveBy(direction * Step);
        }

        /// <summary>Moves the list by pixels, down for a number above 0.</summary>
        public bool MoveBy(float pixels)
        {
            var room = Room;
            if (room <= 0.5f) return false;
            // 1 = the top of the list
            _scroll.verticalNormalizedPosition = 1f - Mathf.Clamp01((Down + pixels) / room);
            Seat();
            return true;
        }

        /// <summary>How far the list can move (pixels).</summary>
        public float Room => _scroll != null && _scroll.content != null ? Mathf.Max(0f, _scroll.content.rect.height - ((RectTransform)_scroll.transform).rect.height) : 0f;

        /// <summary>How far down the list is (pixels).</summary>
        public float Down => Room > 0.5f ? (1f - Mathf.Clamp01(_scroll.verticalNormalizedPosition)) * Room : 0f;

        // the rail was pressed or dragged at this share of its height from the top
        internal void PutAt(float share)
        {
            if (Room <= 0.5f) return;
            _scroll.verticalNormalizedPosition = 1f - Mathf.Clamp01(share);
            Seat();
        }

        // after the scroll view has moved its list
        private void LateUpdate() => Seat();

        private void Seat()
        {
            if (Rail == null || _scroll == null) return;
            var scrolls = Scrolls;
            _shown = Always || scrolls;
            if (Rail.gameObject.activeSelf != _shown) Rail.gameObject.SetActive(_shown);
            foreach (var arrow in _arrows)
                if (arrow != null && arrow.activeSelf != _shown) arrow.SetActive(_shown);
            if (!_shown || _pip == null) return;
            // a list that fits rests at its top
            var share = scrolls ? Mathf.Clamp01(1f - _scroll.verticalNormalizedPosition) : 0f;
            var anchor = new Vector2(0.5f, 1f - share);
            if (_pip.anchorMin != anchor)
            {
                _pip.anchorMin = anchor;
                _pip.anchorMax = anchor;
            }
        }

        /// <summary>For tests: the bar as it stands.</summary>
        public object Snapshot()
        {
            var arrows = 0;
            foreach (var arrow in _arrows)
                if (arrow != null && arrow.activeSelf) arrows++;
            return new { shown = _shown, always = Always, scrolls = Scrolls, room = Room, down = Down, step = Step, arrows, railHeight = Rail != null ? Rail.rect.height : 0f };
        }
    }

    /// <summary>The rail of a <see cref="Dd1ScrollBar"/> under the pointer: pressed or dragged, the list goes where the pointer is.</summary>
    internal class Dd1ScrollRail : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        public Dd1ScrollBar Bar;

        public void OnPointerDown(PointerEventData eventData) => Put(eventData);

        public void OnDrag(PointerEventData eventData) => Put(eventData);

        private void Put(PointerEventData eventData)
        {
            var rail = (RectTransform)transform;
            if (Bar == null || eventData.button != PointerEventData.InputButton.Left) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rail, eventData.position, eventData.pressEventCamera, out var local)) return;
            var area = rail.rect;
            if (area.height > 0f) Bar.PutAt((area.yMax - local.y) / area.height);
        }
    }

    /// <summary>
    /// DD1's look of a picture under the pointer: the art multiplied by a colour of DD1's table whose channels
    /// pass 1 (button_highlight 1.5 1.5 1.3 for a button, inventory_selected 1.4 1.4 1.7 for an item). A UI
    /// picture's own colour cannot pass white, so it is drawn with a material of the UI's shader whose tint is
    /// that factor while the pointer rests on it; pressed, or while it cannot be used, it is not lit. The town's
    /// side of what Dungeon/RaidUi's RaidHighlight does for the raid screen's buttons, for any factor and for
    /// a picture with pictures on it.
    /// </summary>
    internal class Dd1Highlight : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();

        /// <summary>The button the picture is; null for a picture that is lit whenever the pointer is on it.</summary>
        public Selectable Button;
        /// <summary>DD1's colour id of the factor, and DD1's stock value of it.</summary>
        public string Factor = "button_highlight";
        public Color Stock = new Color(1.5f, 1.5f, 1.3f, 1f);
        /// <summary>The pictures drawn inside this one are lit with it (a trinket's icon on its card).</summary>
        public bool WithChildren;

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
            _lit = lit;
            var material = lit ? Lit() : null;
            if (WithChildren)
            {
                foreach (var image in GetComponentsInChildren<Image>(true)) image.material = material;
                return;
            }
            var graphic = GetComponent<Graphic>();
            if (graphic != null) graphic.material = material;
        }

        private Material Lit()
        {
            if (Materials.TryGetValue(Factor, out var material) && material != null) return material;
            material = new Material(Graphic.defaultGraphicMaterial) { name = "DD2Estate." + Factor, hideFlags = HideFlags.HideAndDontSave };
            material.color = Dd1Fonts.Factor(Factor, Stock);
            Materials[Factor] = material;
            return material;
        }
    }

    /// <summary>
    /// The hamlet's tooltip: a heading over a text in a box of DD2's look (<see cref="Dd2TooltipBox"/>: DD2's
    /// ground, font, sizes and colours, read from the game's own tooltip prefab). It was DD1's box until the
    /// batch of 2026-10-06 and keeps the name its callers know. One box per window; whoever shows it owns it
    /// until it is hidden again. The places callers give are DD1's (a corner of the box against the thing it
    /// speaks of); DD2's box is as large as its words, so a width asked for is only the widest a line may be.
    /// </summary>
    internal class Dd1Tooltip
    {
        private const float DefaultWidth = 300f;
        // What DD1's box added to its text's width (the old box's room left and right of the words): callers
        // that stood a tooltip left of its thing counted with it.
        private const float Dd1Box = 36f;

        private readonly Dd2TooltipBox _box;
        private readonly float _rightEdge, _bottomEdge;
        private object _owner;

        /// <param name="rightEdge">The box stays left of this and above <paramref name="bottomEdge"/> (frame pixels; 0 = no limit but the screen's).</param>
        public Dd1Tooltip(Transform frame, float rightEdge, float bottomEdge = 0f)
        {
            _rightEdge = rightEdge;
            _bottomEdge = bottomEdge;
            _box = new Dd2TooltipBox(frame);
        }

        /// <summary>
        /// Shows the box over a place: its lower left corner at <paramref name="bottomLeft"/> (frame pixels), from
        /// where it grows upwards; over the middle of its owner when the owner is the thing under that corner.
        /// </summary>
        public void Show(object owner, Vector2 bottomLeft, string title, string body)
        {
            var size = Fill(owner, title, body, DefaultWidth);
            Place(_box.Over(owner, bottomLeft, size), size);
        }

        /// <summary>
        /// Shows the box centred over a point: the middle of its lower edge at <paramref name="bottomCentre"/>
        /// (frame pixels), as DD2 stands a tooltip over a small button.
        /// </summary>
        public void ShowOver(object owner, Vector2 bottomCentre, string title, string body)
        {
            var size = Fill(owner, title, body, DefaultWidth);
            Place(new Vector2(Mathf.Round(bottomCentre.x - size.x * 0.5f), bottomCentre.y - size.y), size);
        }

        /// <summary>
        /// Shows the box with its upper left corner at <paramref name="topLeft"/>, as DD1's layout files place a
        /// tooltip; a line of the text is <paramref name="textWidth"/> wide at most (a layout's
        /// tooltip_text_width, counted in DD1's font: <see cref="Dd2TooltipLook.Width"/> makes it DD2's).
        /// <paramref name="autoWidth"/> and <paramref name="fixedWidth"/> were DD1's boxes of one width whatever
        /// they said; DD2's box is never wider than its words.
        /// </summary>
        public void ShowAt(object owner, Vector2 topLeft, string title, string body, float textWidth = DefaultWidth, bool autoWidth = true, bool fixedWidth = false)
        {
            var size = Fill(owner, title, body, textWidth);
            var room = _box.Room(_rightEdge, _bottomEdge);
            // DD1's layouts stand this corner beside the thing; DD2's wider box must not come to lie on it
            _box.Place(_box.Beside(owner, topLeft, size, textWidth + Dd1Box, room), size, room);
        }

        /// <summary>
        /// Shows the box left of a place: its upper right corner at <paramref name="topRight"/> (frame pixels).
        /// For a thing whose tooltip stands on its left (a row of the roster): DD2's box is as wide as its words,
        /// so only the edge that faces the thing can be told beforehand. With <paramref name="thingHeight"/> (the
        /// thing begins at the corner and is so tall) the box stands level with the thing's middle, as DD2's
        /// tooltips stand beside a thing.
        /// </summary>
        public void ShowLeftOf(object owner, Vector2 topRight, string title, string body, float textWidth = DefaultWidth, float thingHeight = 0f)
        {
            var size = Fill(owner, title, body, textWidth);
            Place(new Vector2(topRight.x - size.x, thingHeight > 0f ? Mathf.Round(topRight.y + (thingHeight - size.y) * 0.5f) : topRight.y), size);
        }

        public void Hide(object owner)
        {
            if (owner != null && !ReferenceEquals(owner, _owner)) return;
            _owner = null;
            _box.Hide();
        }

        private Vector2 Fill(object owner, string title, string body, float textWidth)
        {
            _owner = owner;
            return _box.Fill(Dd2TooltipLook.Compose(title, body), Dd2TooltipLook.Width(textWidth));
        }

        // Inside the screen, and inside the frame's own limits (a window keeps its tooltip off the roster column).
        private void Place(Vector2 topLeft, Vector2 size)
        {
            _box.Place(topLeft, size, _box.Room(_rightEdge, _bottomEdge));
        }
    }
}
