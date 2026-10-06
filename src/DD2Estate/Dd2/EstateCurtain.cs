using DD2Estate.UI;
using UnityEngine;
using UnityEngine.UI;

namespace DD2Estate.Dd2
{
    /// <summary>
    /// A black sheet of the mod's own over everything but the game's fader, for the way into the Estate. The game
    /// lifts its fade as soon as its hub mode is entered, a moment before the hub is "in" and the hamlet (or the
    /// expedition out of a save) is put up: the fade lifted over nothing and the screen popped in after it. The sheet
    /// is dropped while the game's fader is black and lifted once the hub's screen is up, so that screen comes up
    /// out of the dark, as the dungeon does after a fight.
    /// </summary>
    internal static class EstateCurtain
    {
        /// <summary>Under the game's fader (32766) and its throbber, over everything else.</summary>
        private const int Order = 32000;
        /// <summary>A sheet nobody lifted is lifted after this long: a black screen for good is worse than a pop.</summary>
        private const float Patience = 45f;

        private static Canvas _canvas;
        private static Image _sheet;
        private static float _from, _to, _progress, _seconds = 0.01f, _droppedAt;
        private static bool _moving;

        public static bool Down => _sheet != null && _canvas.enabled && _to >= 1f;

        public static void Drop(float seconds) => Go(1f, seconds);

        public static void Lift(float seconds)
        {
            if (_sheet == null || !_canvas.enabled) return;
            Go(0f, seconds);
        }

        private static void Go(float to, float seconds)
        {
            if (_sheet == null)
            {
                _canvas = UiKit.Canvas("DD2Estate.Curtain", Order);
                // nothing is clicked through a black screen, and nothing waits behind it for a click
                var raycaster = _canvas.GetComponent<GraphicRaycaster>();
                if (raycaster != null) Object.Destroy(raycaster);
                _sheet = UiKit.Image("Sheet", _canvas.transform, null, new Color(0f, 0f, 0f, 0f));
                _sheet.raycastTarget = false;
                UiKit.Stretch((RectTransform)_sheet.transform);
            }
            _canvas.enabled = true;
            _from = _sheet.color.a;
            _to = to;
            _progress = 0f;
            _seconds = Mathf.Max(0.01f, seconds);
            _moving = true;
            if (to >= 1f) _droppedAt = Time.unscaledTime;
            if (seconds > 0f) return;
            _sheet.color = new Color(0f, 0f, 0f, to);
            _moving = false;
            if (to <= 0f) _canvas.enabled = false;
        }

        public static void Tick()
        {
            if (_sheet == null || !_canvas.enabled) return;
            if (_moving)
            {
                // the frame the hamlet is built in is a long one (its art is read): a long frame is not a long step
                _progress = Mathf.Min(1f, _progress + Mathf.Min(Time.unscaledDeltaTime, 1f / 30f) / _seconds);
                _sheet.color = new Color(0f, 0f, 0f, Mathf.Lerp(_from, _to, _progress));
                if (_progress < 1f) return;
                _moving = false;
                if (_to <= 0f) _canvas.enabled = false;
            }
            else if (_to >= 1f && Time.unscaledTime - _droppedAt > Patience)
            {
                Plugin.Log.LogWarning("Estate: the black sheet of the way in was left down; lifted");
                Lift(0.5f);
            }
        }
    }
}
