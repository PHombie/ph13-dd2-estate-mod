using DD2Estate.Dd1;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DD2Estate.Estate
{
    /// <summary>
    /// Sits on a hamlet building's idle image and makes only its painted pixels hoverable and clickable. The
    /// building images are large overlapping rectangles (the sanitarium stands behind the tavern, the nomad
    /// wagon in front of the guild), so the pointer has to fall through their transparent parts.
    /// </summary>
    internal class HamletBuildingHit : MonoBehaviour, ICanvasRaycastFilter, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public HamletScene Scene;
        public string Id;
        public SpineAtlas.AlphaMask Mask;

        public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
        {
            if (Mask == null) return true;
            var rt = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screenPoint, eventCamera, out var local)) return false;
            var rect = rt.rect;
            return Mask.Solid((local.x - rect.xMin) / rect.width, (local.y - rect.yMin) / rect.height);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (Scene != null) Scene.Hover(Id, true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (Scene != null) Scene.Hover(Id, false);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (Scene != null && eventData.button == PointerEventData.InputButton.Left) Scene.Click(Id);
        }
    }
}
