using DD2Estate.UI;
using UnityEngine;
using UnityEngine.UI;

namespace DD2Estate.Estate
{
    /// <summary>
    /// The darkening of the town behind a hamlet panel (a building's window, a town event, the activity log).
    /// A panel ends at the roster column and is cut there by a mask, and so was its shade: between the darkened
    /// town and the column's own gradient (clear at its left edge) a strip of the town stood out in full light,
    /// and the mask's soft edge left one more column of pixels half shaded. The shade is drawn here instead: one
    /// piece across the whole screen, on a layer just beneath the roster column, which lies over it as it lies
    /// over the town. The panel keeps a clear image of its own for the clicks.
    /// </summary>
    internal class HamletShade : MonoBehaviour
    {
        public static readonly Color Colour = new Color(0f, 0f, 0f, 0.72f);

        private GameObject _drawn;

        /// <summary><paramref name="hit"/>: the panel's own shade image (a child of the panel's root), which goes clear.</summary>
        public static void Attach(Image hit)
        {
            if (hit == null) return;
            if (RosterPanel.CanvasRoot == null || !(hit.transform.parent is RectTransform)) return;    // it stays as it was
            hit.color = Color.clear;
            hit.gameObject.AddComponent<HamletShade>().Draw();
        }

        private void Draw()
        {
            var canvas = RosterPanel.CanvasRoot;
            var panel = (RectTransform)transform.parent;
            var image = UiKit.Image(panel.name + ".Shade", canvas, null, Colour);
            var rect = (RectTransform)image.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            // as high as the panel: it ends above the estate's bar
            rect.offsetMin = new Vector2(0f, panel.offsetMin.y);
            rect.offsetMax = new Vector2(0f, panel.offsetMax.y);
            var roster = canvas.Find("Roster");
            var below = roster != null ? roster : panel;
            rect.SetSiblingIndex(below.GetSiblingIndex());
            _drawn = image.gameObject;
        }

        private void OnEnable()
        {
            if (_drawn != null) _drawn.SetActive(true);
        }

        private void OnDisable()
        {
            if (_drawn != null) _drawn.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_drawn != null) Destroy(_drawn);
        }
    }
}
