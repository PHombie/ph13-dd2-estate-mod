using System;
using System.Collections.Generic;
using System.Globalization;
using Assets.Code.UI;
using Assets.Code.UI.Transitions;
using DD2Estate.Dungeon;
using DD2Estate.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD2Estate.Dd2
{
    /// <summary>
    /// The loading screen as DD2 lays one out, with DD1's picture, name and tip (the owner, 2026-10-06: "make
    /// them as in DD2", "DD2's layout, DD1's art").
    ///
    /// What DD2's own loading screen is (read from the running game, <see cref="Dd2Fader"/>, `loading.dd2`): the
    /// screen fader's canvas (order 32766) closes black over the screen with a wipe of ink
    /// (Red Hook/UI/UIInkDissolve; 1 to 1.2 s) or a plain fade (0.67 s); while the game loads the screen is that
    /// black and nothing else but the sign in the lower right corner: a torch in a crown's ring with sparks
    /// (the fader's "Throbber", 100x100 at 800,-300 from the middle of a 1920x1080 canvas, opening over 0.92 s,
    /// turning in a loop of 3 s, closing over 0.5 s); the narrator's line, if he speaks, stands at the foot in
    /// the middle (SubtitlesMgr: a soft dark band 1420x80, 415 under the middle, the words in ND Dunkel Bold 30,
    /// a dull gold); then another wipe lifts the black. It has no picture, no name of a place, no tip, and it
    /// asks for no key: it leaves when what it covers is ready.
    ///
    /// What the mod makes of it: the game's fader IS the screen. Its own wipe closes (the one DD2 uses for the
    /// same kind of journey: the road's for an expedition, the inn's for the way home), its own sign turns in
    /// its own corner, and its own wipe lifts. Into the black, under the sign, go DD1's three things, fading in
    /// and out as DD2's subtitle does: the place's loading picture over the whole screen, its name in DD2's
    /// title font, and DD1's tip set as DD2 sets the narrator's line (a copy of that very object: band, font,
    /// size, colour, place).
    /// </summary>
    internal class Dd2LoadingLook
    {
        // ---- the mod's own numbers (DD2 has no picture, name or tip on its loading screen) -----------------------

        /// <summary>
        /// GUESS (the mod's own, by eye): the middle of the name's line on the 1920x1080 screen (y down). DD1's
        /// loading pictures keep their upper 180 pixels dark for DD1's title plate; the name stands in that dark,
        /// as far from the top as DD2's subtitle is from the foot.
        /// </summary>
        public static Vector2 TitleAt = new Vector2(960f, 118f);
        /// <summary>GUESS (by eye): the name's size, of DD2's title font (its capitals come to 45 pixels; DD1's title has 37).</summary>
        public static float TitleSize = 64f;
        /// <summary>
        /// One of the looks DD2 gives its title font (a material preset beside the font, "NDDunkelD-Bold SDF
        /// DropShadow"); empty, the stock: the subtitle's own. (DD2's looks for the name of a place on the road,
        /// "... TitleCard_Large" and "_Small", are black letters with a grey glow, made for a bright road: seen on
        /// the loading black they cannot be read.)
        /// </summary>
        public static string TitlePreset = "";
        /// <summary>The name's colour; alpha 0: the colour the subtitle's words have.</summary>
        public static Color TitleColour = new Color(0f, 0f, 0f, 0f);
        /// <summary>The middle of the tip's band: where DD2's subtitle stands (SubtitlesMgr's LowerPosition: 415 under the screen's middle).</summary>
        public static Vector2 TipAt = new Vector2(960f, 955f);
        /// <summary>The tip's size; 0: the subtitle's own (30).</summary>
        public static float TipSize = 0f;
        /// <summary>The soft dark band DD2 lays under a subtitle.</summary>
        public static bool Band = true;
        /// <summary>How long the picture and the words take to come out of the black and to go back into it (DD2's subtitle: a third of a second each way).</summary>
        public static float FadeIn = 0.5f, FadeOut = 0.35f;
        /// <summary>
        /// The game's transitions by the kind of journey, as DD2 itself pairs them (GameModeType): the road's
        /// (DRIVING: WIPE_TO_RIGHT, both ways RIGHT_TO_LEFT) for an expedition, the inn's (INN: WIPE_TO_TOP, both
        /// ways TOP_TO_BOTTOM) for the way home.
        /// </summary>
        public static string RaidClose = "RIGHT_TO_LEFT", RaidLift = "RIGHT_TO_LEFT", TownClose = "TOP_TO_BOTTOM", TownLift = "TOP_TO_BOTTOM";
        /// <summary>
        /// The town's picture is dark at its head and foot only: it runs bright into the screen's sides, where
        /// DD1's dungeon pictures are painted into the dark all round (the owner, 2026-10-06: the Estate's loading
        /// picture "looks so-so, the Ruins' is fine"; and, of a round vignette tried first: "rather a black glow
        /// inwards from the edges ... above all a soft passage out of the band"). So black is laid over it from
        /// the edges of what is painted inwards: all of <see cref="EdgeDark"/> at an edge, none
        /// <see cref="EdgeDarkSide"/> pixels in from a side and <see cref="EdgeDarkHead"/> in from the head and the
        /// foot (pixels of the 1920x1080 picture). The head and the foot are not the screen's: the picture has
        /// black bands of its own there, <see cref="EdgeBandHead"/> and <see cref="EdgeBandFoot"/> high (MEASURED
        /// in the town's picture: paint from row 148 to row 866), and it ended on them in a hard line; the black
        /// is whole at a band's edge and thins from there. The widths are by eye.
        /// </summary>
        public static float EdgeDarkSide = 320f, EdgeDarkHead = 200f, EdgeDark = 1f, EdgeBandHead = 148f, EdgeBandFoot = 214f;

        // The subtitle's own numbers, for when the object cannot be copied (FALLBACK).
        private static readonly Vector2 BandSize = new Vector2(1420f, 80f), WordsInset = new Vector2(150f, 15f);
        private const float SubtitleSize = 30f;
        private static readonly Color SubtitleColour = new Color(0.69f, 0.58f, 0.37f, 1f), BandColour = new Color(0f, 0f, 0f, 0.59f);
        private const string TitleFont = "NDDunkelD-Bold SDF";

        public RectTransform Root { get; private set; }
        public bool Whole => Root != null;

        private RectTransform _screen, _band;
        private CanvasGroup _group;
        private TextMeshProUGUI _title, _tip;
        private string _presetWanted;

        public static TransitionType CloseType(LoadingScreen.Kind kind) => Type(kind == LoadingScreen.Kind.Raid ? RaidClose : TownClose);
        public static TransitionType LiftType(LoadingScreen.Kind kind) => Type(kind == LoadingScreen.Kind.Raid ? RaidLift : TownLift);

        private static TransitionType Type(string name)
        {
            return TransitionType.TryToCast(name, out var type) && type != TransitionType.SIZE && type != TransitionType.MANUAL ? type : TransitionType.FADE;
        }

        /// <summary>
        /// Builds the picture, the name and the tip inside the game's fader, between its black and its sign, not
        /// yet seen (alpha 0). Null when the game has no fader to build into, or the building failed.
        /// </summary>
        public static Dd2LoadingLook Build(Sprite picture, string title, string tip, bool darkEdges = false)
        {
            var fader = Dd2Fader.Fader;
            if (fader == null) return null;
            var look = new Dd2LoadingLook();
            try
            {
                look.Make(fader, picture, title, tip, darkEdges);
                return look;
            }
            catch (Exception e)
            {
                // nothing half made is left in the game's fader; the caller puts DD1's screen up instead
                Plugin.Log.LogError("Loading screen: DD2's look could not be built: " + e);
                look.Destroy();
                return null;
            }
        }

        private static Sprite _edges;
        private static string _edgesOf;

        // Black with its alpha falling from the four edges inwards; small, the screen stretches it smooth.
        private static Sprite Edges()
        {
            var of = EdgeDarkSide + " " + EdgeDarkHead + " " + EdgeDark + " " + EdgeBandHead + " " + EdgeBandFoot;
            if (_edges != null && _edgesOf == of) return _edges;
            const int w = 384, h = 216;
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[w * h];
            for (var y = 0; y < h; y++)
            {
                // a texture's first row is the picture's foot
                var fromHead = Mathf.Max(0f, Mathf.Min((y + 0.5f) / h * 1080f - EdgeBandFoot, (h - y - 0.5f) / h * 1080f - EdgeBandHead));
                var clearHead = EdgeDarkHead > 0f ? Mathf.SmoothStep(0f, 1f, fromHead / EdgeDarkHead) : 1f;
                for (var x = 0; x < w; x++)
                {
                    var fromSide = Mathf.Min(x + 0.5f, w - x - 0.5f) / w * 1920f;
                    var clearSide = EdgeDarkSide > 0f ? Mathf.SmoothStep(0f, 1f, fromSide / EdgeDarkSide) : 1f;
                    // the two glows meet in a corner without a crease
                    var dark = (1f - clearHead * clearSide) * EdgeDark;
                    pixels[y * w + x] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(Mathf.Clamp01(dark) * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            _edges = Sprite.Create(texture, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f), 100f);
            _edges.name = "DD2Estate.LoadingEdges";
            _edges.hideFlags = HideFlags.HideAndDontSave;
            _edgesOf = of;
            return _edges;
        }

        private void Make(ScreenFaderBhv fader, Sprite picture, string title, string tip, bool darkEdges)
        {
            Root = UiKit.Rect("DD2Estate.Loading", fader.transform).Stretch();
            // off while it is put together: what is copied from the game must not wake before it is stripped
            Root.gameObject.SetActive(false);
            var throbber = Dd2Fader.Throbber(fader);
            if (throbber != null && throbber.transform.parent == fader.transform) Root.SetSiblingIndex(throbber.transform.GetSiblingIndex());
            _group = Root.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;

            // DD1's 1920x1080 screen, as high as the canvas (the mod's own canvases fit the height too)
            _screen = UiKit.Rect("Screen", Root);
            _screen.Place(RaidUi.Middle, RaidUi.Middle, Vector2.zero, new Vector2(1920f, 1080f));
            Fit();
            var back = UiKit.Image("Picture", _screen, picture);
            ((RectTransform)back.transform).Stretch();
            if (darkEdges && EdgeDark > 0f)
            {
                var shade = UiKit.Image("Edges", _screen, Edges());
                ((RectTransform)shade.transform).Stretch();
            }

            var template = Dd2Fader.SubtitleTemplate();
            var words = template != null ? template.GetComponentInChildren<TextMeshProUGUI>(true) : null;

            // the tip: DD2's subtitle, band and words
            if (template != null && words != null)
            {
                _band = (RectTransform)UnityEngine.Object.Instantiate(template.gameObject, _screen, false).transform;
                _band.name = "Tip";
                Strip(_band.gameObject);
                _tip = _band.GetComponentInChildren<TextMeshProUGUI>(true);
                _tip.name = "Words";
            }
            else
            {
                var band = UiKit.Image("Tip", _screen, null, BandColour);
                _band = (RectTransform)band.transform;
                _tip = Words("Words", _band, null, SubtitleSize, SubtitleColour);
                var inner = (RectTransform)_tip.transform;
                inner.Stretch();
                inner.offsetMin = WordsInset;
                inner.offsetMax = -WordsInset;
            }
            _band.Place(RaidUi.Middle, RaidUi.Middle, new Vector2(TipAt.x - 960f, 540f - TipAt.y), template != null ? template.rect.size : BandSize);
            _band.localScale = Vector3.one;
            var ground = _band.GetComponent<Image>();
            if (ground != null) ground.enabled = Band;
            var bandGroup = _band.GetComponent<CanvasGroup>();
            if (bandGroup != null) bandGroup.alpha = 1f;
            _tip.richText = true;
            _tip.textWrappingMode = TextWrappingModes.Normal;
            _tip.overflowMode = TextOverflowModes.Overflow;
            _tip.alignment = TextAlignmentOptions.Center;
            if (TipSize > 0f) _tip.fontSize = TipSize;
            _tip.text = RaidText.Marks(tip ?? "");
            _band.gameObject.SetActive(!string.IsNullOrEmpty(tip));

            // the name: the same font, as DD2 sets the name of a place
            if (words != null)
            {
                _title = UnityEngine.Object.Instantiate(words.gameObject, _screen, false).GetComponent<TextMeshProUGUI>();
                _title.name = "Title";
                Strip(_title.gameObject);
            }
            else _title = Words("Title", _screen, null, TitleSize, SubtitleColour);
            ((RectTransform)_title.transform).Place(RaidUi.Middle, RaidUi.Middle, new Vector2(TitleAt.x - 960f, 540f - TitleAt.y), new Vector2(1600f, Mathf.Ceil(TitleSize * 1.5f)));
            _title.transform.localScale = Vector3.one;
            _title.richText = false;
            _title.textWrappingMode = TextWrappingModes.NoWrap;
            _title.overflowMode = TextOverflowModes.Overflow;
            _title.alignment = TextAlignmentOptions.Center;
            _title.enableAutoSizing = false;
            _title.fontSize = TitleSize;
            _title.lineSpacing = 0f;
            if (TitleColour.a > 0f) _title.color = TitleColour;
            _title.text = title ?? "";
            _title.gameObject.SetActive(!string.IsNullOrEmpty(title));
            _presetWanted = TitlePreset;
            Preset();

            Root.gameObject.SetActive(true);
            Settle();
        }

        // A copy of a game object keeps its looks and loses its life: what fed it words (the game's data context)
        // is taken off before it wakes.
        private static void Strip(GameObject copy)
        {
            foreach (var behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour != null && !(behaviour is Graphic)) UnityEngine.Object.DestroyImmediate(behaviour);
            copy.SetActive(true);
            foreach (Transform child in copy.transform) child.gameObject.SetActive(true);
        }

        private static TextMeshProUGUI Words(string name, Transform parent, string text, float size, Color colour)
        {
            var rt = UiKit.Rect(name, parent);
            var label = rt.gameObject.AddComponent<TextMeshProUGUI>();
            TMP_FontAsset font = null;
            foreach (var asset in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                if (asset != null && asset.name == TitleFont) font = asset;
            if (font == null) font = UiKit.Font;
            if (font != null) label.font = font;
            label.fontSize = size;
            label.color = colour;
            label.raycastTarget = false;
            label.text = text ?? "";
            return label;
        }

        // The title's look is asked of the game's asset system the first time and is there a moment later.
        private void Preset()
        {
            if (_title == null || string.IsNullOrEmpty(_presetWanted)) return;
            var material = Dd2Fader.FontPreset(_presetWanted);
            if (material == null) return;
            _presetWanted = null;
            if (_title.font != null && material.HasProperty("_MainTex") && material.GetTexture("_MainTex") != _title.font.atlasTexture)
            {
                Plugin.Log.LogWarning("Loading screen: DD2's font look '" + material.name + "' is of another font sheet than '" + _title.font.name + "': not used");
                return;
            }
            _title.fontSharedMaterial = material;
        }

        // A tip of more than one line (DD2's subtitle is one line in a band of 80; a third of DD1's tips are longer
        // than its 1120 pixels): the lines are made as even as they come, so that no last line is a single word
        // (the words' box is narrowed for as long as the count of lines stays), and the band grows with them.
        private void Settle()
        {
            if (_tip == null || !_band.gameObject.activeSelf) return;
            _tip.ForceMeshUpdate();
            var lines = Lines();
            if (lines <= 1) return;
            var box = (RectTransform)_tip.transform;
            Vector2 min = box.offsetMin, max = box.offsetMax;
            var width = box.rect.width;
            var height = _tip.GetPreferredValues(_tip.text, width, 0f).y;
            var kept = 0f;
            for (var inset = EvenStep; width - 2f * inset >= width * 0.5f; inset += EvenStep)
            {
                if (_tip.GetPreferredValues(_tip.text, width - 2f * inset, 0f).y > height + 0.5f) break;
                kept = inset;
            }
            box.offsetMin = new Vector2(min.x + kept, min.y);
            box.offsetMax = new Vector2(max.x - kept, max.y);
            _tip.ForceMeshUpdate();
            if (Lines() > lines)
            {
                // the measure and the setting did not agree: as it was
                box.offsetMin = min;
                box.offsetMax = max;
                _tip.ForceMeshUpdate();
            }
            var face = _tip.font != null ? _tip.font.faceInfo : default;
            var line = face.pointSize > 0 ? face.lineHeight / face.pointSize * _tip.fontSize + _tip.lineSpacing * 0.01f * _tip.fontSize : _tip.fontSize * 1.2f;
            _band.sizeDelta = new Vector2(_band.sizeDelta.x, _band.sizeDelta.y + Mathf.Ceil(line * (lines - 1)));
        }

        private const float EvenStep = 10f;

        private int Lines() => Mathf.Max(1, _tip.textInfo != null ? _tip.textInfo.lineCount : 1);

        // The canvas of the game's fader is not the mod's: it is scaled by width and height both.
        private void Fit()
        {
            var canvas = Root.parent as RectTransform;
            var height = canvas != null ? canvas.rect.height : 1080f;
            _screen.localScale = Vector3.one * (height > 0f ? height / 1080f : 1f);
        }

        public float Alpha
        {
            get => _group != null ? _group.alpha : 0f;
            set
            {
                if (_group == null) return;
                _group.alpha = Mathf.Clamp01(value);
                if (_presetWanted != null) Preset();
            }
        }

        public void Destroy()
        {
            if (Root != null)
            {
                Root.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(Root.gameObject);
            }
            Root = null;
        }

        // ---- dev bridge ----------------------------------------------------------------------------------------

        public static object Numbers()
        {
            return new
            {
                titleAt = Pt(TitleAt), titleSize = TitleSize, titlePreset = TitlePreset, titleColour = Rgba(TitleColour), tipAt = Pt(TipAt), tipSize = TipSize, band = Band,
                fadeIn = FadeIn, fadeOut = FadeOut, raidClose = RaidClose, raidLift = RaidLift, townClose = TownClose, townLift = TownLift,
                edgeSide = EdgeDarkSide, edgeHead = EdgeDarkHead, edgeDark = EdgeDark, bandHead = EdgeBandHead, bandFoot = EdgeBandFoot
            };
        }

        /// <summary>Where each part stands on the 1920x1080 screen (top left corner, y down) as built, and how its words are set.</summary>
        public object Parts()
        {
            var parts = new List<object>();
            if (Root == null) return parts;
            foreach (var name in new[] { "Picture", "Title", "Tip", "Tip/Words" })
            {
                var rt = _screen.Find(name) as RectTransform;
                if (rt == null) continue;
                var corners = new Vector3[4];
                rt.GetWorldCorners(corners);
                Vector3 a = _screen.InverseTransformPoint(corners[0]), b = _screen.InverseTransformPoint(corners[2]);
                var text = rt.GetComponent<TextMeshProUGUI>();
                parts.Add(new
                {
                    name,
                    x = Mathf.Min(a.x, b.x) + 960f, y = 540f - Mathf.Max(a.y, b.y), w = Mathf.Abs(b.x - a.x), h = Mathf.Abs(b.y - a.y),
                    shown = rt.gameObject.activeInHierarchy,
                    font = text != null && text.font != null ? text.font.name : null,
                    size = text != null ? text.fontSize : 0f,
                    colour = text != null ? Rgba(text.color) : null,
                    material = text != null && text.fontSharedMaterial != null ? text.fontSharedMaterial.name : null,
                    lines = text != null && text.textInfo != null ? text.textInfo.lineCount : 0,
                    words = text != null ? text.text : null
                });
            }
            var fader = Dd2Fader.Fader;
            var throbber = Dd2Fader.Throbber(fader);
            if (throbber != null)
            {
                var p = _screen.InverseTransformPoint(throbber.transform.position);
                parts.Add(new { name = "Sign (the game's own)", x = p.x + 960f, y = 540f - p.y, w = 0f, h = 0f, shown = throbber.gameObject.activeInHierarchy, font = (string)null, size = 0f, colour = (string)null, material = (string)null, lines = 0, words = (string)null });
            }
            return parts;
        }

        private static object Pt(Vector2 v) => new { x = v.x, y = v.y };

        private static string Rgba(Color c)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:0.##},{1:0.##},{2:0.##},{3:0.##}", c.r, c.g, c.b, c.a);
        }
    }
}
