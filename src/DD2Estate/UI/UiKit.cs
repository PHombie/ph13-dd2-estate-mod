using System;
using DD2Estate.Dd1;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DD2Estate.UI
{
    /// <summary>
    /// The parts DD1 gives its four text fonts (fonts/fonts.darkest). A label of one of them is drawn pixel for
    /// pixel as in DD1 unless a size is asked for.
    /// </summary>
    internal enum Dd1Text
    {
        /// <summary>DwarvenAxe large: names of screens and buildings, the estate's name, the gold count.</summary>
        Title,
        /// <summary>DwarvenAxe medium: names of heroes, quests and activities, counters.</summary>
        Header,
        /// <summary>Ubuntu medium: text that carries weight (a hero's class, a dialog's question).</summary>
        Body,
        /// <summary>Ubuntu small: descriptions, tooltips, prices.</summary>
        Small,
        /// <summary>Ubuntu medium: levels and counts on badges and icons.</summary>
        Numbers
    }

    /// <summary>
    /// Small uGUI builders for the mod's own screens. Text is set in DD1's text styles, read from the player's
    /// DD1 install (<see cref="Dd1Fonts"/>): each style's font at its measures and in its colour, drawn in DD2's
    /// own faces ([Look] Fonts = dd2, <see cref="Dd2Fonts"/>) or in DD1's bitmaps (dd1). DD2's font as it is
    /// stands in where a DD1 font cannot be had and for any glyph a DD1 bitmap lacks.
    /// </summary>
    internal static class UiKit
    {
        /// <summary>DD2's font, taken from a native label the first time a game screen is patched.</summary>
        public static TMP_FontAsset Font;

        /// <summary>False: every label in DD2's font, as before the DD1 fonts were read.</summary>
        public static bool UseDd1Fonts = true;

        public static readonly Color Parchment = new Color(0.87f, 0.82f, 0.70f);
        public static readonly Color Gold = new Color(0.86f, 0.71f, 0.36f);
        public static readonly Color Blood = new Color(0.60f, 0.08f, 0.06f);
        public static readonly Color Ink = new Color(0.04f, 0.035f, 0.03f);

        /// <summary>DD1's three text colours (colours/base.colours.darkest): running text, names and numbers, warnings.</summary>
        public static Color Neutral => Dd1Fonts.Colour("neutral", Parchment);
        public static Color Notable => Dd1Fonts.Colour("notable", Gold);
        public static Color Harmful => Dd1Fonts.Colour("harmful", Blood);

        // Text asked for by size alone takes the DD1 font that is used at that weight: DD1 has no small
        // DwarvenAxe (it is hard to read there) and no large Ubuntu. A size is DD2's: in either game's font the
        // capitals are at most 0.7 of it tall and a line at most 1.2, so the boxes made for DD2's font hold.
        private const float TitleFrom = 40f;
        private const float HeaderFrom = 24f;
        // FALLBACK sizes of the five parts, used when the DD1 font cannot be had: its native sizes.
        private static readonly float[] StockSizes = { 53f, 34f, 24f, 23f, 24f };
        private static bool _fontsAsked;

        public static Canvas Canvas(string name, int sortingOrder)
        {
            var go = new GameObject(name, typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(go);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        public static RectTransform Stretch(this RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        /// <summary>Anchors and pivots at one point of the parent, positioned in reference pixels.</summary>
        public static RectTransform Place(this RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            return rt;
        }

        /// <summary>
        /// Positions a rect in its parent's pixel space measured from the top-left corner with y down, the way
        /// DD1's layout files are written. <paramref name="pivot"/> is the point of the rect put there.
        /// </summary>
        public static RectTransform PlaceTopLeft(this RectTransform rt, Vector2 position, Vector2 pivot, Vector2 size)
        {
            return rt.Place(new Vector2(0f, 1f), pivot, new Vector2(position.x, -position.y), size);
        }

        public static Image Image(string name, Transform parent, Sprite sprite, Color? color = null, bool raycast = false)
        {
            var rt = Rect(name, parent);
            var image = rt.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color ?? Color.white;
            image.raycastTarget = raycast;
            return image;
        }

        /// <summary>
        /// A piece of DD1 art at its own size, its top-left corner at <paramref name="topLeft"/> of the parent
        /// (y down). If the file is missing the image is hidden, or a flat <paramref name="fallback"/> box of
        /// <paramref name="size"/> where something must stay visible.
        /// </summary>
        public static Image Art(string name, Transform parent, string dd1File, Vector2 topLeft, Vector2? size = null, Color? fallback = null, bool raycast = false)
        {
            // Asking a missing install for a file only fills the log.
            var sprite = Dd1Install.Found ? Dd1Install.Sprite(dd1File) : null;
            var image = Image(name, parent, sprite, sprite != null ? Color.white : fallback ?? Color.clear, raycast);
            ((RectTransform)image.transform).PlaceTopLeft(topLeft, new Vector2(0f, 1f), sprite != null ? sprite.rect.size : size ?? Vector2.zero);
            return image;
        }

        // ---- text --------------------------------------------------------------------------------------

        /// <summary>
        /// A label of a given size. The font is DD1's for that size: DwarvenAxe large from 40, DwarvenAxe medium
        /// from 24, Ubuntu below. Ask for a part or a DD1 style where that guess is wrong.
        /// </summary>
        public static TextMeshProUGUI Text(string name, Transform parent, string text, float size, Color? color = null, TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var label = Label(name, parent, color ?? Parchment, align);
            SetFont(label, FontOf(size >= TitleFrom ? Dd1Text.Title : size >= HeaderFrom ? Dd1Text.Header : Dd1Text.Small), size);
            label.text = text;
            return label;
        }

        /// <summary>
        /// A label in one of DD1's fonts by the part it plays, at the font's native size (1:1, as in DD1) unless
        /// <paramref name="size"/> is given.
        /// </summary>
        public static TextMeshProUGUI Text(string name, Transform parent, string text, Dd1Text part, Color? color = null, TextAlignmentOptions align = TextAlignmentOptions.Center, float size = 0f)
        {
            var label = Label(name, parent, color ?? Parchment, align);
            Style(label, part, size);
            label.text = text;
            return label;
        }

        /// <summary>
        /// A label in a DD1 text style ("town_roster_name"): the font fonts.darkest gives the style at its native
        /// size, and the colour base.colours.darkest gives it unless <paramref name="color"/> is passed.
        /// </summary>
        public static TextMeshProUGUI Text(string name, Transform parent, string text, string dd1Style, Color? color = null, TextAlignmentOptions align = TextAlignmentOptions.Center, float size = 0f)
        {
            var label = Label(name, parent, color ?? Dd1Fonts.Colour(dd1Style, Parchment), align);
            Style(label, dd1Style, size);
            label.text = text;
            return label;
        }

        /// <summary>Sets an existing label in one of DD1's fonts; size 0 = the font's native size.</summary>
        public static void Style(TextMeshProUGUI label, Dd1Text part, float size = 0f)
        {
            SetFont(label, FontOf(part), size > 0f ? size : NativeSize(part));
        }

        /// <summary>Sets an existing label in the font of a DD1 text style; its colour is left alone.</summary>
        public static void Style(TextMeshProUGUI label, string dd1Style, float size = 0f)
        {
            Dd1Font font = null;
            if (UseDd1Fonts)
            {
                Warm();
                font = Dd1Fonts.Style(dd1Style);
            }
            SetFont(label, font, size > 0f ? size : font != null ? font.NativeSize : StockSizes[(int)Dd1Text.Body]);
        }

        /// <summary>The size at which a part's font is drawn 1:1.</summary>
        public static float NativeSize(Dd1Text part)
        {
            var font = FontOf(part);
            return font != null ? font.NativeSize : StockSizes[(int)part];
        }

        private static Dd1Font FontOf(Dd1Text part)
        {
            if (!UseDd1Fonts) return null;
            Warm();
            switch (part)
            {
                case Dd1Text.Title: return Dd1Fonts.Font(Dd1Fonts.Large);
                case Dd1Text.Header: return Dd1Fonts.Font(Dd1Fonts.Medium);
                case Dd1Text.Small: return Dd1Fonts.Font(Dd1Fonts.TextSmall);
                default: return Dd1Fonts.Font(Dd1Fonts.TextMedium);
            }
        }

        private static TextMeshProUGUI Label(string name, Transform parent, Color color, TextAlignmentOptions align)
        {
            var rt = Rect(name, parent);
            var label = rt.gameObject.AddComponent<TextMeshProUGUI>();
            label.color = color;
            label.alignment = align;
            label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.Normal;
            var sprites = GameSprites();
            if (sprites != null) label.spriteAsset = sprites;
            return label;
        }

        // The game's own words (what a quirk, a trinket or a skill does) carry its small pictures as
        // <sprite name="token_stress">. Its labels are given the sheet those are drawn from in their prefabs; a
        // label made here has none, and the tag would stand in the text as written. The sheet is looked for among
        // what the game has loaded: the one that knows the most of the names its strings use.
        private static TMP_SpriteAsset _gameSprites;
        private static float _spritesAsked = -100f;

        private static TMP_SpriteAsset GameSprites()
        {
            if (_gameSprites != null || Time.unscaledTime - _spritesAsked < 5f) return _gameSprites;
            _spritesAsked = Time.unscaledTime;
            try
            {
                var most = 0;
                foreach (var asset in Resources.FindObjectsOfTypeAll<TMP_SpriteAsset>())
                {
                    var table = asset != null ? asset.spriteCharacterTable : null;
                    if (table == null) continue;
                    var known = false;
                    foreach (var sprite in table)
                        if (sprite != null && (sprite.name == "token_stress" || sprite.name == "icon_debuff")) { known = true; break; }
                    if (!known || table.Count <= most) continue;
                    most = table.Count;
                    _gameSprites = asset;
                }
                if (_gameSprites != null) Plugin.Log.LogInfo("UI: the game's text pictures are drawn from '" + _gameSprites.name + "' (" + most + " pictures)");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("UI: the game's sheet of text pictures could not be looked for: " + e.Message);
            }
            return _gameSprites;
        }

        // All four DD1 fonts are made before the first label of a session, not one by one as styles turn up.
        private static void Warm()
        {
            if (_fontsAsked) return;
            _fontsAsked = true;
            Dd1Fonts.Preload();
        }

        // A label always ends up with a font: the DD1 one when it is whole (in the look that is on: DD2's face
        // at DD1's measures, or DD1's bitmap), else DD2's, else the one TMP gave the label when it was made.
        // Nothing here ever assigns null.
        private static void SetFont(TextMeshProUGUI label, Dd1Font font, float size)
        {
            if (font != null && font.Asset != null)
            {
                Dd1Fonts.Fallback(font, Font);
                Dd1Fonts.Set(label, font.Asset);
            }
            else if (Font != null) label.font = Font;
            label.fontSize = size;
        }

        public static Button Button(string name, Transform parent, string text, float fontSize, Action onClick, Sprite background = null)
        {
            var image = Image(name, parent, background, background != null ? Color.white : new Color(0f, 0f, 0f, 0.55f), true);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.highlightedColor = new Color(1f, 0.9f, 0.7f);
            colors.pressedColor = new Color(0.8f, 0.6f, 0.4f);
            button.colors = colors;
            if (onClick != null)
                button.onClick.AddListener(() =>
                {
                    // DD1's click, for every button of the mod's screens (silent without DD1 audio)
                    DD2Estate.Dd2.EstateAudio.Ui("ui/town/button_click");
                    onClick();
                });
            if (!string.IsNullOrEmpty(text))
            {
                var label = Text("Label", image.transform, text, fontSize);
                ((RectTransform)label.transform).Stretch();
            }
            return button;
        }

        /// <summary>Tells when the pointer comes over a graphic (or anything drawn inside it) and when it leaves.</summary>
        public static UiHover Hover(GameObject target, Action<bool> changed)
        {
            var hover = target.GetComponent<UiHover>();
            if (hover == null) hover = target.AddComponent<UiHover>();
            hover.Changed += changed;
            return hover;
        }
    }

    internal class UiHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public event Action<bool> Changed;

        public bool Inside { get; private set; }

        public void OnPointerEnter(PointerEventData eventData) => Set(true);

        public void OnPointerExit(PointerEventData eventData) => Set(false);

        // A hidden object gets no exit event: the pointer would seem to rest on it for ever.
        private void OnDisable() => Set(false);

        private void Set(bool inside)
        {
            if (Inside == inside) return;
            Inside = inside;
            Changed?.Invoke(inside);
        }
    }
}
