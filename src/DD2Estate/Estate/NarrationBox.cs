using DD2Estate.Dd1;
using DD2Estate.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DD2Estate.Estate
{
    /// <summary>
    /// The Ancestor's words as text, set the way DD1 sets its subtitles in town: DwarvenAxe large in DD1's gold,
    /// centred on the subtitle position over the dark band that rises from the foot of the screen
    /// (shared/app.darkest: s_TownSubtitleContextTunables and s_SubtitleGlobalTunables;
    /// shared/subtitles/subtitle_bg.png). That position lies on the estate's bar, as in DD1, and the band is
    /// DD1's one wash: what lies under it shows through the words as it does there. The band fades in and out
    /// (background_fade_in_time, background_fade_out_time); the words come and go at once. DD1 speaks the lines
    /// and takes them away itself (the voice is played from the DD1 install: Dd2/EstateAudio); a line without
    /// a voice stays until a click or until whoever showed it hides it. As in DD1 the words
    /// never stand between the player and the screen: the band takes no clicks, a click anywhere sends the
    /// line away and still lands on whatever was under the pointer (the Provision button lies under the band). (DD1's voice lines
    /// live in old FMOD banks the game cannot play; the story text is the mod's own.)
    ///
    /// One band for the whole mod: it stands on the canvas of whoever shows a text and moves when that changes.
    /// </summary>
    internal class NarrationBox : MonoBehaviour
    {
        private const float MinimumShown = 0.25f;   // a click that opened a screen must not close the line it brought up
        private const string App = "shared/app.darkest";
        private const string BandArt = "shared/subtitles/subtitle_bg.png";
        private const string Style = "subtitle_context_town";
        private const float ScreenWidth = 1920f, ScreenHeight = 1080f;
        private const float BandHeight = 240f;      // subtitle_bg.png (1920x240)
        private const float Foot = 24f;             // a long text stops this far above the screen's edge instead of running off it

        /// <summary>
        /// Until this time (Time.unscaledTime) a click does not send the line away: a spoken line stays while
        /// the voice lasts. Set by whoever shows a spoken line; every new text starts without it.
        /// </summary>
        public static float KeepUntil;

        private static NarrationBox _instance;
        private RectTransform _band;
        private UnityEngine.UI.Image _wash;
        private Color _washColour;
        private TextMeshProUGUI _text;
        private Vector2 _centre;
        private float _width, _bandRise, _fadeIn, _fadeOut;
        private float _shownAt;
        // How much of the band is there (0..1), and whether its words have been taken away and it is fading out.
        private float _shade;
        private bool _leaving;

        /// <summary>A line is on the screen; false again as soon as its words are gone, while the band still fades.</summary>
        public static bool IsOpen => _instance != null && _instance.gameObject.activeSelf && !_instance._leaving;

        public static void Show(Transform canvasRoot, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (_instance == null) _instance = Build(canvasRoot);
            // The hamlet and the dungeon have a canvas each: the band goes where the speaker is.
            else if (canvasRoot != null && _instance.transform.parent != canvasRoot) _instance.transform.SetParent(canvasRoot, false);
            // The band comes up out of nothing, or back from where it had faded to; the words are there at once.
            if (!_instance.gameObject.activeSelf) _instance._shade = 0f;
            _instance._leaving = false;
            _instance.Shade();
            _instance._text.gameObject.SetActive(true);
            _instance.gameObject.SetActive(true);
            _instance.transform.SetAsLastSibling();
            _instance._shownAt = Time.unscaledTime;
            KeepUntil = 0f;
            _instance.Set(text);
        }

        public static void Hide()
        {
            if (_instance == null || !_instance.gameObject.activeSelf || _instance._leaving) return;
            // DD1 takes the words away and lets the band fade (background_fade_out_time). A band on a canvas
            // that is switched off has nobody to watch it go.
            _instance._leaving = true;
            _instance._text.gameObject.SetActive(false);
            if (_instance._fadeOut <= 0f || !_instance.gameObject.activeInHierarchy) _instance.gameObject.SetActive(false);
        }

        private static NarrationBox Build(Transform canvasRoot)
        {
            var app = Dd1Install.Found ? DarkestFile.Load(App) : null;
            var sprite = Dd1Install.Found ? Dd1Install.Sprite(BandArt) : null;
            var back = UiKit.Image("Narration", canvasRoot, sprite, sprite != null ? Color.white : new Color(0.03f, 0.025f, 0.02f, 0.9f), true);
            var box = back.gameObject.AddComponent<NarrationBox>();
            // The band spans the foot of the screen; its height follows the text.
            box._band = (RectTransform)back.transform;
            box._band.anchorMin = new Vector2(0f, 0f);
            box._band.anchorMax = new Vector2(1f, 0f);
            box._band.pivot = new Vector2(0.5f, 0f);
            box._centre = HamletScreen.Offset(app, "s_TownSubtitleContextTunables", "screen_position", 960f, 990f);
            box._width = app?.Find("s_TownSubtitleContextTunables")?.Float("max_width", 0, 1280f) ?? 1280f;
            // FALLBACK numbers: DD1's own values of s_SubtitleGlobalTunables.
            var tunables = app?.Find("s_SubtitleGlobalTunables");
            box._bandRise = tunables?.Float("background_y_offset", 0, 100f) ?? 100f;
            box._fadeIn = tunables?.Float("background_fade_in_time", 0, 0.5f) ?? 0.5f;
            box._fadeOut = tunables?.Float("background_fade_out_time", 0, 0.5f) ?? 0.5f;
            box._wash = back;
            box._washColour = back.color;

            box._text = UiKit.Text("Text", back.transform, "", Style, null, TextAlignmentOptions.Top);
            // nothing of the band is in the pointer's way
            foreach (var graphic in back.GetComponentsInChildren<UnityEngine.UI.Graphic>(true)) graphic.raycastTarget = false;
            return box;
        }

        private void Set(string words)
        {
            _text.text = words;
            // DD1 centres its one or two lines on the subtitle position and starts the band a fixed way above
            // that. A longer text keeps its foot on the screen and pushes the band up.
            var height = _text.GetPreferredValues(words, _width, 0f).y;
            var line = _text.font != null ? _text.font.faceInfo.lineHeight * _text.fontSize / _text.font.faceInfo.pointSize * _text.font.faceInfo.scale : _text.fontSize;
            var top = Mathf.Min(_centre.y - height * 0.5f, ScreenHeight - Foot - height);
            var bandTop = top - (_bandRise - line * 0.5f);
            // The art is not squeezed under a short text: what does not fit hangs below the screen's edge.
            var rise = ScreenHeight - bandTop;
            _band.offsetMin = new Vector2(0f, rise - Mathf.Max(BandHeight, rise));
            _band.offsetMax = new Vector2(0f, rise);
            ((RectTransform)_text.transform).Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(_centre.x - ScreenWidth * 0.5f, bandTop - top), new Vector2(_width, height + 4f));
        }

        private void Shade()
        {
            _wash.color = new Color(_washColour.r, _washColour.g, _washColour.b, _washColour.a * _shade);
        }

        private void Update()
        {
            if (_leaving)
            {
                _shade = _fadeOut > 0f ? Mathf.Max(0f, _shade - Time.unscaledDeltaTime / _fadeOut) : 0f;
                Shade();
                if (_shade <= 0f) gameObject.SetActive(false);
                return;
            }
            if (_shade < 1f)
            {
                _shade = _fadeIn > 0f ? Mathf.Min(1f, _shade + Time.unscaledDeltaTime / _fadeIn) : 1f;
                Shade();
            }
            if (Time.unscaledTime - _shownAt < MinimumShown || Time.unscaledTime < KeepUntil) return;
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)) Hide();
        }
    }
}
