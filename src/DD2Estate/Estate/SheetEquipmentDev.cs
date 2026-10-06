using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Assets.Code.UI.Managers;
using Assets.Code.Utils;
using DD2Estate.Dev;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace DD2Estate.Estate
{
    /// <summary>
    /// Test commands of the hero sheet's Equipment block for the dev bridge: what the block shows and where it
    /// and the Trinket Inventory stand (equipment.state), what DD2's character sheet is made of and where its
    /// parts are on screen (equipment.dump, equipment.canvases), and a real drag of the mouse from one place to
    /// another (equipment.drag), which is how a trinket goes from the inventory onto the sheet and back.
    /// </summary>
    [EstateModule]
    internal static class SheetEquipmentDev
    {
        private static void Register()
        {
            // The block on the sheet that is up: hero, weapon and armour (level, DD1's picture, what the level gives),
            // the two slots (what is worn, where they are on screen), where sheet and panel stand; and the panel.
            AgentBridge.Register("equipment.state", o => new { sheet = SheetEquipment.Describe(), panel = RealmInventoryPanel.Snapshot(), top = TopCard() });
            // The heading over the block until the game is closed: {"heading":"dd1"} DD1's bar, {"heading":"dd2"} the sheet's own.
            AgentBridge.Register("equipment.heading", o => SheetEquipment.SetHeading((string)o["heading"]));
            // Where things are for a drag (real pixels from the bottom left): {"id":"..."} a card of the stores; the
            // sheet's two slots are in equipment.state.
            AgentBridge.Register("equipment.card", o =>
            {
                var at = RealmInventoryPanel.CardAt((string)o["id"]);
                return at != null ? (object)new { x = at.Value.x, y = at.Value.y } : "no such card on the panel";
            });
            // A real drag of the left button, as the mouse would do it: {"x":..,"y":..,"tox":..,"toy":..} in screen
            // pixels from the bottom left, {"seconds":0.6}; {"hold":true} stops with the button still down, a little
            // short of the end (for a picture of the card in the hand), {"release":true} lets go where the pointer is.
            // The game hears it only while its window has the focus.
            AgentBridge.Register("equipment.drag", o =>
            {
                if ((bool?)o["release"] == true) Plugin.Host.StartCoroutine(Release());
                else Plugin.Host.StartCoroutine(Drag(new Vector2((float)o["x"], (float)o["y"]), new Vector2((float)o["tox"], (float)o["toy"]), (float?)o["seconds"] ?? 0.6f, (bool?)o["hold"] ?? false));
                return new { focused = Application.isFocused };
            });
            // The game's frame for a look at another size: {"width":1706,"height":960} a window of that size;
            // {"full":true} the borderless full screen of the display again. Nothing of it is written to DD2's options.
            AgentBridge.Register("equipment.window", o =>
            {
                if ((bool?)o["full"] == true) Screen.SetResolution(Display.main.systemWidth, Display.main.systemHeight, FullScreenMode.FullScreenWindow);
                else if (o["width"] != null && o["height"] != null) Screen.SetResolution((int)o["width"], (int)o["height"], FullScreenMode.Windowed);
                return new { screen = new[] { Screen.width, Screen.height }, mode = Screen.fullScreenMode.ToString(), display = new[] { Display.main.systemWidth, Display.main.systemHeight } };
            });
            // What the pointer would take at a place (real pixels from the bottom left): the first things under it, front to back.
            AgentBridge.Register("equipment.under", o =>
            {
                var system = EventSystem.current;
                if (system == null) return "no event system";
                var hits = new List<RaycastResult>();
                system.RaycastAll(new PointerEventData(system) { position = new Vector2((float)o["x"], (float)o["y"]) }, hits);
                return hits.Take(6).Select(hit => AgentBridge.PathOf(hit.gameObject.transform)).ToArray();
            });
            // The tree under DD2's character sheet (or under {"path":"..."}), {"depth":6}: every object with its
            // components, its box on screen in the mod's pixels (1080 high, from the top left), its text and picture.
            AgentBridge.Register("equipment.dump", o =>
            {
                var path = (string)o["path"];
                Transform root;
                if (!string.IsNullOrEmpty(path))
                {
                    var found = AgentBridge.FindObject(path);
                    if (found == null) return "not found: " + path;
                    root = found.transform;
                }
                else
                {
                    var screen = SingletonMonoBehaviour<CommonUiBhv>.Instance.GetCharacterSheetInstance();
                    if (screen == null) return "no character sheet is open";
                    root = screen.transform;
                }
                var text = new StringBuilder();
                text.Append("# ").Append(AgentBridge.PathOf(root)).Append('\n');
                Dump(root, 0, (int?)o["depth"] ?? 6, (bool?)o["off"] ?? true, text);
                return text.ToString();
            });
            // Every canvas that is drawing: how it is rendered, its order, its camera, its scale.
            AgentBridge.Register("equipment.canvases", o =>
            {
                var list = new List<object>();
                foreach (var canvas in Object.FindObjectsOfType<Canvas>())
                {
                    if (!canvas.isActiveAndEnabled) continue;
                    if (!canvas.isRootCanvas && !canvas.overrideSorting) continue;
                    var scaler = canvas.GetComponent<CanvasScaler>();
                    var camera = canvas.worldCamera;
                    list.Add(new
                    {
                        path = AgentBridge.PathOf(canvas.transform), root = canvas.isRootCanvas, mode = canvas.renderMode.ToString(), order = canvas.sortingOrder,
                        layer = canvas.sortingLayerName, overrides = canvas.overrideSorting, plane = canvas.planeDistance,
                        camera = camera != null ? camera.name + " depth " + camera.depth : null, scale = canvas.scaleFactor,
                        size = ((RectTransform)canvas.transform).rect.size.ToString(),
                        scaler = scaler != null ? scaler.uiScaleMode + " " + scaler.referenceResolution + " match " + scaler.matchWidthOrHeight + " " + scaler.screenMatchMode : null,
                        raycaster = canvas.GetComponent<GraphicRaycaster>() != null
                    });
                }
                return new { screen = new[] { Screen.width, Screen.height }, canvases = list };
            });
        }

        // The card in the player's hand, if one is: where it is on screen.
        private static object TopCard()
        {
            var layer = SheetEquipment.Overlay;
            var ghost = layer != null ? layer.Find("DraggedTrinket") as RectTransform : null;
            return ghost != null ? SheetEquipment.Box(ghost) : null;
        }

        private static IEnumerator Drag(Vector2 from, Vector2 to, float seconds, bool hold)
        {
            var mouse = Mouse.current;
            if (mouse == null) yield break;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = from });
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = from }.WithButton(MouseButton.Left));
            yield return null;
            yield return null;
            var started = Time.unscaledTime;
            var end = hold ? 0.9f : 1f;
            while (true)
            {
                var share = Mathf.Clamp01((Time.unscaledTime - started) / Mathf.Max(0.05f, seconds));
                InputSystem.QueueStateEvent(mouse, new MouseState { position = Vector2.Lerp(from, to, share * end) }.WithButton(MouseButton.Left));
                yield return null;
                if (share >= 1f) break;
            }
            if (hold) yield break;
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = to }.WithButton(MouseButton.Left));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = to });
        }

        private static IEnumerator Release()
        {
            var mouse = Mouse.current;
            if (mouse == null) yield break;
            var at = mouse.position.ReadValue();
            InputSystem.QueueStateEvent(mouse, new MouseState { position = at }.WithButton(MouseButton.Left));
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = at });
        }

        private static void Dump(Transform t, int level, int depth, bool withOff, StringBuilder text)
        {
            if (!withOff && !t.gameObject.activeInHierarchy) return;
            text.Append(' ', level * 2).Append(t.gameObject.activeInHierarchy ? "" : "(off) ").Append(t.name).Append("  [");
            text.Append(string.Join(",", t.GetComponents<Component>().Where(c => c != null && !(c is Transform) && !(c is CanvasRenderer)).Select(c => c.GetType().Name)));
            text.Append("]");
            if (t is RectTransform rect) text.Append(' ').Append(SheetEquipment.Box(rect));
            var label = t.GetComponent<TMP_Text>();
            if (label != null && !string.IsNullOrWhiteSpace(label.text)) text.Append(" \"").Append(label.text.Replace("\n", " ")).Append("\" ").Append(label.font != null ? label.font.name : "").Append(' ').Append(label.fontSize);
            var image = t.GetComponent<Image>();
            if (image != null) text.Append(" <").Append(image.sprite != null ? image.sprite.name : "-").Append(image.raycastTarget ? " ray" : "").Append(image.enabled ? "" : " disabled").Append('>');
            text.Append('\n');
            if (level >= depth)
            {
                if (t.childCount > 0) text.Append(' ', (level + 1) * 2).Append("... ").Append(t.childCount).Append(" children\n");
                return;
            }
            for (var i = 0; i < t.childCount; i++) Dump(t.GetChild(i), level + 1, depth, withOff, text);
        }
    }
}
