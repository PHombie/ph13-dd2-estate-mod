using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace DD2Estate.Dev
{
    /// <summary>
    /// Feeds synthetic device states into Unity's Input System, so tests exercise the same path as a real
    /// mouse and keyboard (EventSystem raycasts, Keyboard.current) instead of calling handlers directly.
    /// </summary>
    internal static class DevInput
    {
        public static IEnumerator Click(float x, float y)
        {
            var mouse = Mouse.current;
            if (mouse == null) yield break;
            var position = new Vector2(x, y);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = position });
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = position }.WithButton(MouseButton.Left));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = position });
        }

        /// <summary>The pointer goes to a place and stays (hover: tooltips, glows).</summary>
        public static void Move(float x, float y)
        {
            var mouse = Mouse.current;
            if (mouse != null) InputSystem.QueueStateEvent(mouse, new MouseState { position = new Vector2(x, y) });
        }

        private static readonly List<GameObject> Hovered = new List<GameObject>();

        /// <summary>
        /// What the pointer resting at a place would tell the UI, said to it directly: the game does not hear the
        /// synthetic mouse while another window has the focus, and a look at a tooltip must not wait for that.
        /// The thing under the place and everything above it in its tree hear "pointer enter"; what heard it the
        /// last time and is not under the place any more hears "pointer exit". {x, y} in screen pixels from the
        /// bottom left; a place with nothing under it only takes the last hover away.
        /// </summary>
        public static object Hover(float x, float y)
        {
            var system = EventSystem.current;
            if (system == null) return "no event system";
            var data = new PointerEventData(system) { position = new Vector2(x, y) };
            var hits = new List<RaycastResult>();
            system.RaycastAll(data, hits);
            var under = new List<GameObject>();
            if (hits.Count > 0)
                for (var t = hits[0].gameObject.transform; t != null; t = t.parent) under.Add(t.gameObject);
            foreach (var was in Hovered)
                if (was != null && !under.Contains(was)) ExecuteEvents.Execute(was, data, ExecuteEvents.pointerExitHandler);
            foreach (var now in under)
                if (!Hovered.Contains(now)) ExecuteEvents.Execute(now, data, ExecuteEvents.pointerEnterHandler);
            Hovered.Clear();
            Hovered.AddRange(under);
            return new { hit = hits.Count > 0 ? PathOf(hits[0].gameObject.transform) : null, hits = hits.Count };
        }

        private static string PathOf(Transform t)
        {
            var path = t.name;
            for (var p = t.parent; p != null && path.Length < 160; p = p.parent) path = p.name + "/" + path;
            return path;
        }

        public static IEnumerator Hold(Key key, float seconds)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) yield break;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            yield return new WaitForSeconds(seconds);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        }
    }
}
