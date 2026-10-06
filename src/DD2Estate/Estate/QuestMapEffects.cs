using System;
using DD2Estate.Dd1;
using DD2Estate.Dd2;
using DD2Estate.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD2Estate.Estate
{
    /// <summary>
    /// What the estate map and the provision screen share with the hamlet's own chrome. Both are DD1 town
    /// screens: the estate's bar (gold, heirlooms, the row of buttons) stays on screen with them, so their
    /// pictures go under it in the hamlet's canvas instead of over everything, the hamlet's Embark is put away
    /// while they are open, and their own way forward stands where it stood (shared/progression/
    /// progression.layout.darkest: forward_pos, forward_text_offset, forward_selected_overlay_offset).
    /// </summary>
    internal static class TownChrome
    {
        private const string Dir = "shared/progression/";
        private const float ScreenWidth = 1920f, ScreenHeight = 1080f;
        private static readonly Vector2 ForwardSize = new Vector2(312f, 52f);    // progression_forward.png
        private static readonly Vector2 GlowSize = new Vector2(311f, 24f);       // progression_forward_selected_overlay.png
        private const string Embark = "EmbarkButton";                            // HamletScreen.BuildForward

        /// <summary>
        /// Puts a layer of the hamlet's canvas just under the first of these children the canvas has ("Roster",
        /// "EstateSummary"): what the hamlet draws from there on lies over the layer. Without any of them the
        /// layer goes on top.
        /// </summary>
        public static void PutUnder(Transform layer, params string[] siblings)
        {
            var canvas = layer.parent;
            foreach (var name in siblings)
            {
                var above = canvas != null ? canvas.Find(name) : null;
                if (above == null || above == layer) continue;
                var index = above.GetSiblingIndex();
                // taken out from before it, the layer leaves everything after it one place lower
                layer.SetSiblingIndex(layer.GetSiblingIndex() < index ? index - 1 : index);
                return;
            }
            layer.SetAsLastSibling();
        }

        /// <summary>
        /// Puts the hamlet's Embark away, or back: a screen with a way forward of its own stands its button on
        /// the same spot of the bar. True when the button was there to be put away.
        /// </summary>
        public static bool ShowEmbark(Transform canvas, bool show)
        {
            var embark = canvas != null ? canvas.Find(Embark) : null;
            if (embark == null || embark.gameObject.activeSelf == show) return false;
            embark.gameObject.SetActive(show);
            return true;
        }

        /// <summary>
        /// DD1's forward button on the middle of the estate's bar, as the hamlet's Embark is built: its art,
        /// the glow that comes up under the pointer and the word hanging from forward_text_offset (the middle
        /// of the text's top edge). <paramref name="layer"/> is a layer stretched over the hamlet's canvas; the
        /// button keeps the screen's middle on a wider screen, as the bar's own middle does.
        /// </summary>
        public static Button Forward(Transform layer, string word, Action click, out TextMeshProUGUI label)
        {
            var progression = Dd1Ui.Layout(Dir + "progression.layout.darkest");
            var at = Dd1Ui.Offset(progression, "progression_layout", "forward_pos", 801f, 984f);
            var textAt = Dd1Ui.Offset(progression, "progression_layout", "forward_text_offset", 160f, -2f);
            var glowAt = Dd1Ui.Offset(progression, "progression_layout", "forward_selected_overlay_offset", 0f, -13f);

            var art = Dd1Ui.Sprite(Dir + "progression_forward.png");
            var image = UiKit.Image("Forward", layer, art, art != null ? Color.white : new Color(0f, 0f, 0f, 0.55f), true);
            var rect = (RectTransform)image.transform;
            rect.Place(new Vector2(0.5f, 0f), Dd1Ui.TopLeft, new Vector2(at.x - ScreenWidth * 0.5f, ScreenHeight - at.y), art != null ? art.rect.size : ForwardSize);
            // as painted at rest: the glow is what tells the pointer is on it
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colours = button.colors;
            colours.highlightedColor = Color.white;
            colours.selectedColor = Color.white;
            colours.pressedColor = new Color(0.8f, 0.6f, 0.4f);
            colours.disabledColor = Color.white;
            button.colors = colours;
            if (click != null) button.onClick.AddListener(() => click());

            var glow = Dd1Ui.Art("Glow", rect, Dir + "progression_forward_selected_overlay.png", glowAt, GlowSize);
            glow.gameObject.SetActive(false);
            UiKit.Hover(image.gameObject, inside =>
            {
                glow.gameObject.SetActive(inside);
                // DD1's sound of the pointer coming onto the way forward
                if (inside) EstateAudio.Ui("ui/town/button_mouse_over_embark");
            });

            label = UiKit.Text("Label", rect, word, "town_progression_forward", null, TextAlignmentOptions.Top);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            ((RectTransform)label.transform).PlaceTopLeft(textAt, Dd1Ui.TopCentre, new Vector2(ForwardSize.x, 64f));
            return button;
        }

        /// <summary>
        /// DD1 draws a picture button as it is painted and brighter under the pointer (colours/
        /// base.colours.darkest: button_highlight, a factor above 1). A UI picture's own colour cannot pass
        /// white, so the picture is drawn with a material of the UI's shader tinted by that factor while the
        /// pointer rests on it.
        /// </summary>
        public static void Highlight(Graphic picture, bool lit)
        {
            if (picture == null) return;
            if (!lit)
            {
                picture.material = null;
                return;
            }
            if (_lit == null)
            {
                _lit = new Material(Graphic.defaultGraphicMaterial) { name = "DD2Estate.TownHighlight", hideFlags = HideFlags.HideAndDontSave };
                _lit.color = QuestMapColours.Factor("button_highlight", new Color(1.5f, 1.5f, 1.3f, 1f));     // FALLBACK: DD1's stock value
            }
            picture.material = _lit;
        }

        private static Material _lit;
    }

    /// <summary>
    /// DD1's "dd effect" (shared/dd_effects.darkest): once the Darkest Dungeon has been entered, what lies under
    /// the manor shows through a town screen now and then: a picture that is there for effect_length seconds,
    /// jumping every min_max_time_in_state to another size (min_max_scale) and place (up to max_offset pixels
    /// off). The chance of it grows with the descents made (post_dd1_chance .. post_dd4_chance).
    ///
    /// GUESS: the file gives the chance, not how often it is asked; here once a second, each picture for itself.
    /// </summary>
    internal class DdFlicker : MonoBehaviour
    {
        private const string File = "shared/dd_effects.darkest";
        private const float AskEvery = 1f;

        private Graphic _picture;
        private RectTransform _rect;
        private Vector2 _home;
        private float _length, _stateMin, _stateMax, _scaleMin, _scaleMax, _chance, _offset;
        private float _nextAsk, _until, _nextState;

        /// <param name="block">The effect's block in the file ("town_quest_select_dd_effect").</param>
        /// <param name="descents">Darkest Dungeon quests done.</param>
        /// <param name="stock">FALLBACK, DD1's own values of the block: the four chances.</param>
        public static void Attach(Graphic picture, string block, int descents, float[] stock)
        {
            if (picture == null) return;
            picture.enabled = false;
            if (descents <= 0) return;      // nothing has been woken yet
            var layout = Dd1Ui.Layout(File);
            var step = Mathf.Min(descents, 4);
            var chance = Dd1Ui.Number(layout, block, "post_dd" + step + "_chance", stock != null && stock.Length >= step ? stock[step - 1] : 0f);
            if (chance <= 0f) return;
            var flicker = picture.gameObject.AddComponent<DdFlicker>();
            flicker._picture = picture;
            flicker._rect = picture.rectTransform;
            flicker._home = flicker._rect.anchoredPosition;
            flicker._chance = chance;
            flicker._length = Dd1Ui.Number(layout, block, "effect_length", 0.25f);
            var state = Dd1Ui.Offset(layout, block, "min_max_time_in_state", 0.05f, 0.10f);
            flicker._stateMin = Mathf.Max(0.01f, state.x);
            flicker._stateMax = Mathf.Max(flicker._stateMin, state.y);
            var scale = Dd1Ui.Offset(layout, block, "min_max_scale", 1f, 1.2f);
            flicker._scaleMin = scale.x;
            flicker._scaleMax = Mathf.Max(scale.x, scale.y);
            flicker._offset = Dd1Ui.Number(layout, block, "max_offset", 5f);
            // it grows about its own middle
            var rect = flicker._rect;
            var size = rect.sizeDelta;
            var shift = new Vector2((0.5f - rect.pivot.x) * size.x, (0.5f - rect.pivot.y) * size.y);
            rect.pivot = new Vector2(0.5f, 0.5f);
            flicker._home += shift;
            rect.anchoredPosition = flicker._home;
        }

        private void OnEnable()
        {
            _until = 0f;
            _nextAsk = Time.unscaledTime + AskEvery * UnityEngine.Random.value;
            Rest();
        }

        private void Update()
        {
            var now = Time.unscaledTime;
            if (now < _until)
            {
                if (now >= _nextState) Jump(now);
                return;
            }
            if (_picture.enabled) Rest();
            if (now < _nextAsk) return;
            _nextAsk = now + AskEvery;
            if (UnityEngine.Random.value >= _chance) return;
            _until = now + _length;
            _picture.enabled = true;
            Jump(now);
        }

        private void Jump(float now)
        {
            _nextState = now + UnityEngine.Random.Range(_stateMin, _stateMax);
            var scale = UnityEngine.Random.Range(_scaleMin, _scaleMax);
            _rect.localScale = new Vector3(scale, scale, 1f);
            _rect.anchoredPosition = _home + new Vector2(UnityEngine.Random.Range(-_offset, _offset), UnityEngine.Random.Range(-_offset, _offset));
        }

        private void Rest()
        {
            if (_picture == null) return;
            _picture.enabled = false;
            _rect.localScale = Vector3.one;
            _rect.anchoredPosition = _home;
        }
    }

    /// <summary>A picture that comes up out of nothing in a given time, eased like DD1's easeInSine (the town event's picture behind the party tray).</summary>
    internal class FadeIn : MonoBehaviour
    {
        private Graphic _picture;
        private float _from, _seconds;

        public static void Attach(Graphic picture, float seconds)
        {
            if (picture == null || seconds <= 0f) return;
            var fade = picture.gameObject.AddComponent<FadeIn>();
            fade._picture = picture;
            fade._seconds = seconds;
            fade._from = Time.unscaledTime;
            fade.Set(0f);
        }

        private void Update()
        {
            var share = Mathf.Clamp01((Time.unscaledTime - _from) / _seconds);
            Set(1f - Mathf.Cos(share * Mathf.PI * 0.5f));
            if (share >= 1f) Destroy(this);
        }

        private void Set(float alpha)
        {
            var colour = _picture.color;
            colour.a = alpha;
            _picture.color = colour;
        }
    }

    /// <summary>
    /// A region's bar filling up to where it stands now (quest_select.anim.darkest: town_quest_select_xp_bar_anim,
    /// easeInOutSine), with DD1's effect riding the fill's end: fx/dungeon_progress, animation "dungeon_progress".
    /// The mod has no Spine runtime, so the effect's two pictures are taken out of its sheet and moved by hand
    /// along what the skeleton does (read with tools/preview_corridor.py): the glow comes up to 47 px in a fifth
    /// of a second and shrinks to nothing by the end, the sparkle swells to 62 px and shrinks with it; both fade
    /// in over the first 0.3 s and out over the last 0.6 s of the animation's two seconds.
    /// </summary>
    internal class QuestBarFill : MonoBehaviour
    {
        private const string Sheet = "fx/dungeon_progress/dungeon_progress.sprite.png";
        private const float FxSeconds = 2f;                                         // the animation's length
        private static readonly Vector2 GlowArt = new Vector2(50f, 54f), SparkleArt = new Vector2(50f, 53f);
        // (time, scale of the art) as the skeleton draws them
        private static readonly float[] GlowKeys = { 0f, 0f, 0.1f, 0.71f, 0.2f, 0.93f, 0.4f, 0.93f, 1.4f, 0.54f, 1.6f, 0.4f, 1.8f, 0.2f, 2f, 0f };
        private static readonly float[] SparkleKeys = { 0f, 0f, 0.1f, 0.75f, 0.2f, 1.03f, 0.4f, 1.22f, 0.6f, 1.23f, 0.8f, 1.1f, 1f, 0.84f, 1.4f, 0.76f, 1.6f, 0.58f, 1.8f, 0.27f, 2f, 0f };
        private static readonly float[] AlphaKeys = { 0f, 0f, 0.3f, 1f, 1.4f, 1f, 2f, 0f };

        private Image _fill, _glow, _sparkle;
        private float _from, _to, _seconds, _started, _width;
        private Vector2 _tip;

        /// <param name="tip">Where the effect stands when the bar is empty (the bar's corner + dungeon_xp_bar_fx_offset), in the fill's parent.</param>
        public static void Attach(Image fill, float from, float to, float seconds, Vector2 tip, float width)
        {
            if (fill == null || seconds <= 0f || to <= from) return;
            var anim = fill.gameObject.AddComponent<QuestBarFill>();
            anim._fill = fill;
            anim._from = from;
            anim._to = to;
            anim._seconds = seconds;
            anim._tip = tip;
            anim._width = width;
            anim._started = Time.unscaledTime;
            anim._glow = Picture("Glow", fill.transform.parent, "glow", GlowArt);
            anim._sparkle = Picture("Sparkle", fill.transform.parent, "sparkle", SparkleArt);
            fill.fillAmount = from;
            anim.Update();
        }

        private static Image Picture(string name, Transform parent, string region, Vector2 size)
        {
            var sprite = Dd1Ui.Sprite(Sheet + "#" + region);
            if (sprite == null) return null;
            var image = UiKit.Image("BarFx." + name, parent, sprite, Color.clear);
            ((RectTransform)image.transform).PlaceTopLeft(Vector2.zero, Dd1Ui.Middle, size);
            return image;
        }

        private void Update()
        {
            var time = Time.unscaledTime - _started;
            var share = Mathf.Clamp01(time / _seconds);
            var amount = Mathf.Lerp(_from, _to, 0.5f - 0.5f * Mathf.Cos(share * Mathf.PI));
            _fill.fillAmount = amount;
            var at = _tip + new Vector2(_width * amount, 0f);
            var alpha = Key(AlphaKeys, time);
            Pose(_glow, at, GlowArt * Key(GlowKeys, time), alpha, 0f);
            Pose(_sparkle, at, SparkleArt * Key(SparkleKeys, time), alpha, time * 40f);
            if (time < Mathf.Max(_seconds, FxSeconds)) return;
            if (_glow != null) Destroy(_glow.gameObject);
            if (_sparkle != null) Destroy(_sparkle.gameObject);
            Destroy(this);
        }

        private static void Pose(Image picture, Vector2 at, Vector2 size, float alpha, float turn)
        {
            if (picture == null) return;
            var rect = picture.rectTransform;
            rect.anchoredPosition = new Vector2(at.x, -at.y);
            rect.sizeDelta = size;
            rect.localEulerAngles = new Vector3(0f, 0f, turn);
            picture.color = new Color(1f, 1f, 1f, alpha);
        }

        /// <summary>A value along (time, value) pairs, straight between them.</summary>
        internal static float Key(float[] keys, float time)
        {
            if (time <= keys[0]) return keys[1];
            for (var i = 2; i < keys.Length; i += 2)
            {
                if (time > keys[i]) continue;
                var span = keys[i] - keys[i - 2];
                return span <= 0f ? keys[i + 1] : Mathf.Lerp(keys[i - 1], keys[i + 1], (time - keys[i - 2]) / span);
            }
            return keys[keys.Length - 1];
        }
    }

    /// <summary>
    /// What DD1 plays when the four in the tray make a party it has a name for: fx/party_combo, animation "combo"
    /// (one second), with the sound /ui/town/party_comp. Its pictures out of the sheet, moved by hand along the
    /// skeleton: a bar of light 465 px wide on the party's name that is there from a fifth of a second on and
    /// fades over the last 0.4 s, and two balls of light that run from its middle to its ends, shrinking.
    ///
    /// Where it lies: the layout's change_animation_offset is the skeleton's origin and the skeleton keeps its
    /// light 394 px from it, which puts the bar on the party's name (5 px left of and 8 px under the name's own
    /// offset). GUESS: the six sparks of the skeleton are left out; which way they fly from the bar cannot be
    /// told without seeing DD1 play it.
    /// </summary>
    internal class PartyComboFx : MonoBehaviour
    {
        private const string Sheet = "fx/party_combo/party_combo.sprite.png";
        private const float BarOffset = 394.1f;                                     // the skeleton's bar_glow bone from its root
        private static readonly Vector2 BarArt = new Vector2(930f, 84f), BallArt = new Vector2(205f, 197f);
        private static readonly float[] BarWidth = { 0.15f, 0.5f, 0.2f, 0.825f, 0.3f, 1f, 0.6f, 1f, 1f, 0.9f };          // of 465 px
        private static readonly float[] BarAlpha = { 0.15f, 0f, 0.2f, 1f, 0.6f, 1f, 1f, 0f };
        private static readonly float[] BallOut = { 0.15f, 0f, 0.2f, 30f, 0.3f, 72f, 0.4f, 103f, 0.5f, 128f, 0.6f, 150f, 0.7f, 168f, 0.8f, 183f, 0.9f, 194f, 1f, 200f };
        private static readonly float[] BallSize = { 0.15f, 0.9f, 0.2f, 0.85f, 0.3f, 0.64f, 0.4f, 0.49f, 0.5f, 0.36f, 0.6f, 0.25f, 0.7f, 0.16f, 0.8f, 0.085f, 0.9f, 0.03f, 1f, 0f };
        private const float Seconds = 1f;

        private Image _bar, _left, _right;
        private float _started;

        /// <param name="origin">The skeleton's origin in <paramref name="parent"/> (pixels from its top left, y down).</param>
        public static void Play(Transform parent, Vector2 origin, int siblingIndex)
        {
            EstateAudio.Ui("ui/town/party_comp");
            var bar = Dd1Ui.Sprite(Sheet + "#bar_glow");
            var ball = Dd1Ui.Sprite(Sheet + "#inner_glow");
            if (bar == null && ball == null) return;
            var root = UiKit.Rect("PartyCombo", parent).PlaceTopLeft(origin + new Vector2(0f, -BarOffset), Dd1Ui.Middle, Vector2.zero);
            root.SetSiblingIndex(siblingIndex);
            var fx = root.gameObject.AddComponent<PartyComboFx>();
            fx._started = Time.unscaledTime;
            fx._bar = Picture("Bar", root, bar);
            fx._left = Picture("Left", root, ball);
            fx._right = Picture("Right", root, ball);
            fx.Update();
        }

        private static Image Picture(string name, Transform parent, Sprite sprite)
        {
            if (sprite == null) return null;
            var image = UiKit.Image(name, parent, sprite, Color.clear);
            ((RectTransform)image.transform).Place(Dd1Ui.Middle, Dd1Ui.Middle, Vector2.zero, Vector2.zero);
            return image;
        }

        private void Update()
        {
            var time = Time.unscaledTime - _started;
            if (time >= Seconds)
            {
                Destroy(gameObject);
                return;
            }
            var alpha = QuestBarFill.Key(BarAlpha, time);
            if (_bar != null)
            {
                _bar.rectTransform.sizeDelta = BarArt * 0.5f * QuestBarFill.Key(BarWidth, time);
                _bar.color = new Color(1f, 1f, 1f, alpha);
            }
            var away = QuestBarFill.Key(BallOut, time);
            var size = BallArt * QuestBarFill.Key(BallSize, time);
            Ball(_left, -away, size, alpha);
            Ball(_right, away, size, alpha);
        }

        private static void Ball(Image ball, float x, Vector2 size, float alpha)
        {
            if (ball == null) return;
            ball.rectTransform.anchoredPosition = new Vector2(x, 0f);
            ball.rectTransform.sizeDelta = size;
            ball.color = new Color(1f, 1f, 1f, alpha);
        }
    }
}
