using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using DD2Estate.Dd1;
using DD2Estate.Dd2;
using DD2Estate.Dungeon;
using DD2Estate.UI;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DD2Estate.Estate
{
    /// <summary>
    /// DD1's tutorial window, as its files and a real frame of it give it (frames of the running game, 1920x1080:
    /// _lab/dd1_ref/town/tutorial_popup_*.png):
    /// <list type="bullet">
    /// <item>shared/tutorial_popup/tutorial_popup.background.png (1500x800: the frame with the open book and the
    /// ink) with its corner at tutorial_popup_layout base_pos (220, 20) of the 1920x1080 screen; every other
    /// number of the layout counts from that corner.</item>
    /// <item>The picture (400x400) hangs from image_offset by the MIDDLE of its top edge (400, 240): on the frame
    /// it lies at 420..820, left of the words. The title (font and colour "tutorial_popup_title": DwarvenAxe
    /// medium, notable) and the words (Ubuntu small, neutral, 660 wide, centred line by line) hang from
    /// title_offset and description_offset the same way: the frame has the title's middle at 1160 and its line
    /// from 267, the first line of the words from 320, 25 apart.</item>
    /// <item>The close icon (39x39) has its corner at close_button_offset (1272, 132): the ring in the frame's
    /// upper right corner.</item>
    /// <item>Everything behind the window is blurred, not darkened: a Gaussian of about 2.4 pixels at 1080 high
    /// (the hamlet at rest with and without the window, compared; what looks dark in some frames is the
    /// building's own window behind it).</item>
    /// <item>tutorial_popup.animation.darkest: the window comes from 1.1 of its size to 1 in 0.6 s
    /// ("easeInOutElastic") and goes at once. It sounds /ui/shared/tutorial_popup as it comes and
    /// /ui/town/page_close as it goes (exe 0xb9bb78, 0xb9bd3d).</item>
    /// </list>
    /// The picture behind is taken once, when the window comes, and blurred here: the game's pipeline has no blur
    /// for what the estate draws as UI. It stands still under the window (in DD1 the clouds go on).
    /// </summary>
    internal class TutorialPopup : MonoBehaviour
    {
        public const string Dir = "shared/tutorial_popup/";
        private const string LayoutFile = Dir + "tutorial_popup.layout.darkest";
        private const string AnimationFile = Dir + "tutorial_popup.animation.darkest";
        /// <summary>Over the hamlet (5), the camp (5) and the results (6); under the estate's curtain (32000).</summary>
        private const int Order = 20;
        private static readonly Vector2 BackgroundSize = new Vector2(1500f, 800f);
        private static readonly Vector2 CloseSize = new Vector2(39f, 39f);
        private const float WordsHeight = 345f;      // from description_offset down to the frame's inner edge

        /// <summary>The Gaussian DD1 lays over the screen behind, in pixels of a screen 1080 high (measured on its frame).</summary>
        public static float BlurSigma = 2.4f;
        public static bool Blur = true;

        private static TutorialPopup _instance;
        private static int _closedFrame = -1;

        /// <summary>The tutorial on screen or about to be; null when there is none.</summary>
        public static string Current { get; private set; }

        public static bool Busy => Current != null;

        private RectTransform _window;
        private RawImage _behind;
        private Texture2D _blurred;
        private Image _picture;
        private TextMeshProUGUI _title, _words;
        private Vector2 _pictureAt;
        private float _startScale = 1.1f, _seconds = 0.6f, _age;
        private bool _elastic = true, _shown;
        private Coroutine _coming;

        // ---- what the rest of the mod calls ------------------------------------------------------------------

        /// <summary>Puts a tutorial's window up; false when DD1 has neither words nor a picture for it.</summary>
        public static bool Show(string id)
        {
            if (string.IsNullOrEmpty(id) || !Dd1Install.Found) return false;
            var title = Tutorials.Title(id);
            var words = Tutorials.Description(id);
            if (title == null && words == null) return false;
            try
            {
                if (_instance == null) _instance = Build();
                _instance.Come(id, title, words);
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Tutorial: the window of " + id + " could not be built: " + e);
                Close(false);
                return false;
            }
        }

        public static void Close(bool sound = true)
        {
            if (Current == null && (_instance == null || !_instance._shown)) return;
            Current = null;
            _closedFrame = Time.frameCount;
            if (_instance != null) _instance.Go();
            if (sound) EstateAudio.Ui("ui/town/page_close");
        }

        /// <summary>
        /// The game opens its menu on Escape; a tutorial on screen takes the key instead (and the frame it went
        /// in still counts, whichever of the two heard the key first).
        /// </summary>
        internal static bool TakesEscape()
        {
            if (Current != null)
            {
                Close();
                return true;
            }
            return Time.frameCount == _closedFrame;
        }

        public static object Describe()
        {
            if (_instance == null) return new { built = false };
            var s = _instance;
            return new
            {
                built = true,
                shown = s._shown,
                id = Current,
                title = s._title != null ? s._title.text : null,
                words = s._words != null ? s._words.text : null,
                picture = s._picture != null && s._picture.sprite != null ? s._picture.sprite.name : null,
                scale = s._window != null ? s._window.localScale.x : 0f,
                blurred = s._behind != null && s._behind.texture != null && s._behind.color.a > 0.5f,
                wordsSize = s._words != null ? s._words.fontSize : 0f
            };
        }

        // ---- the window ------------------------------------------------------------------------------------------

        private static TutorialPopup Build()
        {
            var canvas = UiKit.Canvas("DD2Estate.Tutorial", Order);
            var popup = canvas.gameObject.AddComponent<TutorialPopup>();
            var root = canvas.transform;

            // FALLBACK numbers: DD1's own values of these entries, used when the file cannot be read.
            var layout = Dd1Ui.Layout(LayoutFile);
            const string block = "tutorial_popup_layout";
            var basePos = Dd1Ui.Offset(layout, block, "base_pos", 220f, 20f);
            var titleAt = Dd1Ui.Offset(layout, block, "title_offset", 940f, 247f);
            popup._pictureAt = Dd1Ui.Offset(layout, block, "image_offset", 400f, 240f);
            var wordsAt = Dd1Ui.Offset(layout, block, "description_offset", 940f, 300f);
            var wordsWidth = Dd1Ui.Number(layout, block, "description_width", 660f);
            var closeAt = Dd1Ui.Offset(layout, block, "close_button_offset", 1272f, 132f);

            var animation = Dd1Ui.Layout(AnimationFile);
            popup._startScale = Dd1Ui.Number(animation, "tutorial_popup_enter_animation", "start_scale", 1.1f);
            popup._seconds = Mathf.Max(0f, Dd1Ui.Number(animation, "tutorial_popup_enter_animation", "duration", 0.6f));
            var easing = animation?.Find("tutorial_popup_enter_animation");
            popup._elastic = string.Equals(easing != null ? easing.String("easing_function", 0, "easeInOutElastic") : "easeInOutElastic", "easeInOutElastic", StringComparison.OrdinalIgnoreCase);

            // What is behind: one picture across the screen, and nothing under it can be clicked.
            var behind = UiKit.Rect("Behind", root).Stretch();
            popup._behind = behind.gameObject.AddComponent<RawImage>();
            popup._behind.color = Color.clear;
            popup._behind.raycastTarget = true;

            // DD1's screen, in the middle of a wider one.
            var centre = new Vector2(0.5f, 0.5f);
            var stage = UiKit.Rect("Stage", root).Place(centre, centre, Vector2.zero, new Vector2(1920f, 1080f));
            // The window grows about its own middle.
            popup._window = UiKit.Rect("Window", stage).PlaceTopLeft(basePos + BackgroundSize * 0.5f, centre, BackgroundSize);
            var window = popup._window;

            Dd1Ui.Art("Background", window, Dir + "tutorial_popup.background.png", Vector2.zero, BackgroundSize, new Color(0.02f, 0.02f, 0.02f, 0.96f));
            popup._picture = UiKit.Image("Picture", window, null, Color.clear);
            popup._title = Dd1Ui.Line("Title", window, "tutorial_popup_title", titleAt, new Vector2(wordsWidth, 46f), TextAlignmentOptions.Top);
            popup._words = Dd1Ui.Block("Words", window, "tutorial_popup_description", wordsAt, new Vector2(wordsWidth, WordsHeight), TextAlignmentOptions.Top);

            // DD1's own way out: the red cross in the ring. Its sound is the window's (page_close), not a button's click.
            var cross = Dd1Ui.Sprite(Dir + "tutorial_popup.close_icon.png");
            var close = UiKit.Image("Close", window, cross, cross != null ? Color.white : new Color(0.55f, 0.05f, 0.03f), true);
            ((RectTransform)close.transform).PlaceTopLeft(closeAt, Dd1Ui.TopLeft, cross != null ? cross.rect.size : CloseSize);
            var button = close.gameObject.AddComponent<Button>();
            button.targetGraphic = close;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => Close());
            close.gameObject.AddComponent<Dd1Highlight>().Button = button;

            popup._behind.gameObject.SetActive(false);
            window.gameObject.SetActive(false);
            return popup;
        }

        private void Come(string id, string title, string words)
        {
            Current = id;
            if (_coming != null) StopCoroutine(_coming);
            _coming = StartCoroutine(Coming(id, title, words));
        }

        private IEnumerator Coming(string id, string title, string words)
        {
            // A window that follows another keeps the picture behind it: a new one would be a picture of the blur.
            if (!_behind.gameObject.activeSelf)
            {
                _window.gameObject.SetActive(false);
                if (Blur)
                {
                    yield return new WaitForEndOfFrame();
                    try { TakeBehind(); }
                    catch (Exception e) { Plugin.Log.LogWarning("Tutorial: the screen behind could not be blurred: " + e.Message); }
                }
                // without a picture it is all but clear, and still takes the clicks
                _behind.color = _behind.texture != null && Blur ? Color.white : new Color(0f, 0f, 0f, 0.004f);
                _behind.gameObject.SetActive(true);
            }
            if (Current != id) yield break;     // closed while the picture was taken

            _title.text = Rich(title ?? "");
            _words.text = Rich(words ?? "");
            var file = Tutorials.Picture(id);
            var art = file != null ? Dd1Install.Sprite(file) : null;
            _picture.sprite = art;
            _picture.color = art != null ? Color.white : Color.clear;
            var size = art != null ? art.rect.size : new Vector2(400f, 400f);
            ((RectTransform)_picture.transform).PlaceTopLeft(new Vector2(_pictureAt.x - size.x * 0.5f, _pictureAt.y), Dd1Ui.TopLeft, size);

            _age = 0f;
            _window.localScale = Vector3.one * (_seconds > 0f ? _startScale : 1f);
            _window.gameObject.SetActive(true);
            _shown = true;
            _coming = null;
            EstateAudio.Ui("ui/shared/tutorial_popup");
            Plugin.Log.LogInfo("Tutorial: " + id + " is shown");
        }

        private void Go()
        {
            if (_coming != null) StopCoroutine(_coming);
            _coming = null;
            _shown = false;
            if (_window != null) _window.gameObject.SetActive(false);
            if (_behind != null)
            {
                _behind.gameObject.SetActive(false);
                _behind.texture = null;
            }
            if (_blurred != null) Destroy(_blurred);
            _blurred = null;
            // the party may walk again (CorridorView.ShowArea's own rule)
            var view = CorridorView.Instance;
            if (view != null) view.InputEnabled = !view.IsRoom;
        }

        private void Update()
        {
            if (Current == null) return;
            // Nothing of the estate is to move under the window: the party stands (the bridge's own walking is not touched).
            var view = CorridorView.Instance;
            if (view != null) view.InputEnabled = false;

            if (!_shown) return;
            if (_age < _seconds)
            {
                _age += Time.unscaledDeltaTime;
                var share = _seconds > 0f ? Mathf.Clamp01(_age / _seconds) : 1f;
                var scale = Mathf.LerpUnclamped(_startScale, 1f, _elastic ? ElasticInOut(share) : share);
                _window.localScale = new Vector3(scale, scale, 1f);
            }
            var keyboard = Keyboard.current;
            // GUESS: Escape closes it, as it closes DD1's other windows (the exe's key handling was not read).
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && Current != null) Close();
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            if (_blurred != null) Destroy(_blurred);
        }

        // Robert Penner's easeInOutElastic, which DD1's animation files name (the period is his: 0.3 * 1.5).
        internal static float ElasticInOut(float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            const float period = 0.45f;
            var s = period / 4f;
            t = t * 2f - 1f;
            if (t < 0f) return -0.5f * Mathf.Pow(2f, 10f * t) * Mathf.Sin((t - s) * 2f * Mathf.PI / period);
            return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t - s) * 2f * Mathf.PI / period) * 0.5f + 1f;
        }

        // ---- DD1's colour marks ---------------------------------------------------------------------------------

        /// <summary>"{colour_start|notable}word{colour_end}" as the label's own colour tags; a colour DD1's table lacks leaves the word as it is.</summary>
        internal static string Rich(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0) return text ?? "";
            const string start = "{colour_start|", end = "{colour_end}";
            var rich = new StringBuilder(text.Length + 64);
            var open = new Stack<bool>();
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == '{')
                {
                    if (string.CompareOrdinal(text, i, start, 0, start.Length) == 0)
                    {
                        var close = text.IndexOf('}', i);
                        if (close > i)
                        {
                            var id = text.Substring(i + start.Length, close - i - start.Length).Trim();
                            var missing = new Color(-1f, -1f, -1f, 1f);
                            var colour = Dd1Fonts.Colour(id, missing);
                            var known = colour.r >= 0f;
                            open.Push(known);
                            if (known) rich.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(colour)).Append('>');
                            i = close;
                            continue;
                        }
                    }
                    else if (string.CompareOrdinal(text, i, end, 0, end.Length) == 0)
                    {
                        if (open.Count > 0 && open.Pop()) rich.Append("</color>");
                        i += end.Length - 1;
                        continue;
                    }
                }
                if (text[i] != '\r') rich.Append(text[i]);
            }
            while (open.Count > 0)
                if (open.Pop()) rich.Append("</color>");
            return rich.ToString();
        }

        // ---- the screen behind ------------------------------------------------------------------------------------

        private void TakeBehind()
        {
            var blurred = BlurredScreen(BlurSigma, "DD2Estate.TutorialBehind");
            if (blurred == null) return;
            if (_blurred != null) Destroy(_blurred);
            _blurred = blurred;
            _behind.texture = _blurred;
        }

        /// <summary>
        /// The frame as it stands on screen, made smaller, blurred and given back as a picture of its own (the
        /// caller destroys it); null when the frame cannot be had. To be asked for at the end of a frame
        /// (WaitForEndOfFrame). The small picture is drawn back at the screen's size, which blurs it once more:
        /// the Gaussian asks for the rest. <paramref name="sigmaAt1080"/>: the Gaussian in pixels of a screen
        /// 1080 high. DD1 lays the same blur under its confirm dialog (the dungeon's retreat question).
        /// </summary>
        internal static Texture2D BlurredScreen(float sigmaAt1080, string name)
        {
            var shot = ScreenCapture.CaptureScreenshotAsTexture();
            if (shot == null) return null;
            Color32[] pixels;
            int width, height;
            try
            {
                pixels = shot.GetPixels32();
                width = shot.width;
                height = shot.height;
            }
            finally { Destroy(shot); }
            if (pixels == null || width < 4 || height < 4 || pixels.Length < width * height) return null;

            var factor = Mathf.Max(1, Mathf.RoundToInt(height / 540f));
            int w = width / factor, h = height / factor;
            var from = new float[w * h * 3];
            var to = new float[w * h * 3];
            var area = 1f / (factor * factor);
            Parallel.For(0, h, y =>
            {
                for (var x = 0; x < w; x++)
                {
                    float r = 0f, g = 0f, b = 0f;
                    for (var dy = 0; dy < factor; dy++)
                    {
                        var row = (y * factor + dy) * width + x * factor;
                        for (var dx = 0; dx < factor; dx++)
                        {
                            var p = pixels[row + dx];
                            r += p.r;
                            g += p.g;
                            b += p.b;
                        }
                    }
                    var at = (y * w + x) * 3;
                    from[at] = r * area;
                    from[at + 1] = g * area;
                    from[at + 2] = b * area;
                }
            });

            // what making it smaller and drawing it larger again blur by themselves (a box and a tent of `factor` pixels)
            var wanted = Mathf.Max(0.1f, sigmaAt1080) * height / 1080f;
            var given = factor > 1 ? (factor * factor - 1) / 12f + factor * factor / 6f : 0f;
            var sigma = Mathf.Sqrt(Mathf.Max(0.04f, wanted * wanted - given)) / factor;
            var radius = Mathf.Max(1, Mathf.CeilToInt(sigma * 3f));
            var kernel = new float[radius * 2 + 1];
            var sum = 0f;
            for (var i = -radius; i <= radius; i++) sum += kernel[i + radius] = Mathf.Exp(-(i * i) / (2f * sigma * sigma));
            for (var i = 0; i < kernel.Length; i++) kernel[i] /= sum;

            Parallel.For(0, h, y =>
            {
                for (var x = 0; x < w; x++)
                {
                    float r = 0f, g = 0f, b = 0f;
                    for (var k = -radius; k <= radius; k++)
                    {
                        var at = (y * w + Math.Min(w - 1, Math.Max(0, x + k))) * 3;
                        var weight = kernel[k + radius];
                        r += from[at] * weight;
                        g += from[at + 1] * weight;
                        b += from[at + 2] * weight;
                    }
                    var into = (y * w + x) * 3;
                    to[into] = r;
                    to[into + 1] = g;
                    to[into + 2] = b;
                }
            });
            var blurred = new Color32[w * h];
            Parallel.For(0, h, y =>
            {
                for (var x = 0; x < w; x++)
                {
                    float r = 0f, g = 0f, b = 0f;
                    for (var k = -radius; k <= radius; k++)
                    {
                        var at = (Math.Min(h - 1, Math.Max(0, y + k)) * w + x) * 3;
                        var weight = kernel[k + radius];
                        r += to[at] * weight;
                        g += to[at + 1] * weight;
                        b += to[at + 2] * weight;
                    }
                    blurred[y * w + x] = new Color32((byte)Mathf.Clamp(r + 0.5f, 0f, 255f), (byte)Mathf.Clamp(g + 0.5f, 0f, 255f), (byte)Mathf.Clamp(b + 0.5f, 0f, 255f), 255);
                }
            });

            var picture = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave
            };
            picture.SetPixels32(blurred);
            picture.Apply(false, true);
            return picture;
        }
    }

    /// <summary>
    /// Escape is the game's key for its menu. While a tutorial is up, or a hero's name is being written, the key
    /// is theirs (it closes the one, gives up the other) and the menu stays shut.
    /// </summary>
    [HarmonyPatch(typeof(Assets.Code.UI.Managers.CommonUiBhv), "TogglePauseMenu")]
    internal static class EscapeBelongsToTheEstatesWindow
    {
        private static bool Prefix()
        {
            try
            {
                if (!EstateSession.Active) return true;
                var tutorial = TutorialPopup.TakesEscape();
                var name = HeroRename.TakesEscape();
                return !(tutorial || name);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Tutorial: the menu's key could not be looked at: " + e.Message);
                return true;
            }
        }
    }
}
