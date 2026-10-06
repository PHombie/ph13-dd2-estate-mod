using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using DD2Estate.Dev;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD2Estate.Estate
{
    /// <summary>
    /// Looking at DD2's own UI from outside, for the work on its character sheet:
    ///   sheet.dump {"type":"CharacterSheetUiBhv" | "path":"A/B/C", "file":"C:/.../sheet.jsonl", "up":true}
    ///       every object under the first live component of that type (or under that path), one JSON object a
    ///       line: path, active, screen rectangle, components, picture (sprite, colour, material), text (font,
    ///       size, colour, the words), a Selectable's colours and sprites, layout numbers, and every Color field
    ///       of the game's own components. "up": start at the screen's root under its canvas.
    ///   sheet.blue {"file":"..."}
    ///       what is drawn in a blue on screen right now: every active Graphic whose colour is blue, and every
    ///       text with a blue colour tag in its words.
    /// </summary>
    [EstateModule]
    internal static class SheetProbe
    {
        private static void Register()
        {
            AgentBridge.Register("sheet.dump", o => Dump(o));
            AgentBridge.Register("sheet.blue", o => Blue((string)o["file"]));
        }

        private static Transform Root(JObject o)
        {
            var path = (string)o["path"];
            if (!string.IsNullOrEmpty(path))
            {
                var go = AgentBridge.FindObject(path);
                return go != null ? go.transform : null;
            }
            var typeName = (string)o["type"] ?? "CharacterSheetUiBhv";
            foreach (var behaviour in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>(true))
            {
                if (behaviour == null || behaviour.GetType().Name != typeName || !behaviour.gameObject.scene.IsValid()) continue;
                var t = behaviour.transform;
                if ((bool?)o["up"] ?? false)
                    while (t.parent != null && t.parent.GetComponent<Canvas>() == null) t = t.parent;
                return t;
            }
            return null;
        }

        private static object Dump(JObject o)
        {
            var root = Root(o);
            if (root == null) return "nothing found";
            var file = (string)o["file"];
            if (string.IsNullOrEmpty(file)) return "no file";
            var count = 0;
            using (var writer = new StreamWriter(file, false, new UTF8Encoding(false)))
                Walk(root, 0, writer, ref count);
            return new { root = AgentBridge.PathOf(root), objects = count, file };
        }

        private static void Walk(Transform t, int depth, StreamWriter writer, ref int count)
        {
            writer.WriteLine(JsonConvert.SerializeObject(Describe(t, depth), Formatting.None));
            count++;
            for (var i = 0; i < t.childCount; i++) Walk(t.GetChild(i), depth + 1, writer, ref count);
        }

        private static Dictionary<string, object> Describe(Transform t, int depth)
        {
            var line = new Dictionary<string, object> { ["d"] = depth, ["name"] = t.name, ["path"] = AgentBridge.PathOf(t), ["on"] = t.gameObject.activeInHierarchy, ["self"] = t.gameObject.activeSelf };
            if (t is RectTransform rect)
            {
                line["rect"] = ScreenRect(rect);
                line["anchored"] = new[] { Round(rect.anchoredPosition.x), Round(rect.anchoredPosition.y), Round(rect.sizeDelta.x), Round(rect.sizeDelta.y) };
                line["anchors"] = new[] { rect.anchorMin.x, rect.anchorMin.y, rect.anchorMax.x, rect.anchorMax.y, rect.pivot.x, rect.pivot.y };
                if (t.localScale != Vector3.one) line["scale"] = new[] { t.localScale.x, t.localScale.y };
            }
            var names = new List<string>();
            foreach (var component in t.GetComponents<Component>())
            {
                if (component == null || component is Transform) continue;
                var type = component.GetType();
                names.Add(type.Name + (component is Behaviour b && !b.enabled ? "(off)" : ""));
                try { Add(line, component, type); }
                catch (Exception e) { line["!" + type.Name] = e.GetType().Name + ": " + e.Message; }
            }
            line["c"] = names;
            return line;
        }

        private static void Add(Dictionary<string, object> line, Component component, Type type)
        {
            switch (component)
            {
                case Image image:
                    line["image"] = new
                    {
                        sprite = image.sprite != null ? image.sprite.name : null,
                        over = image.overrideSprite != null && image.overrideSprite != image.sprite ? image.overrideSprite.name : null,
                        size = image.sprite != null ? new[] { image.sprite.rect.width, image.sprite.rect.height } : null,
                        colour = Hex(image.color), mat = image.material != null ? image.material.name : null, type = image.type.ToString(),
                        fill = image.type == Image.Type.Filled ? (float?)image.fillAmount : null, ray = image.raycastTarget, aspect = image.preserveAspect
                    };
                    break;
                case RawImage raw:
                    line["raw"] = new { texture = raw.texture != null ? raw.texture.name : null, colour = Hex(raw.color), mat = raw.material != null ? raw.material.name : null };
                    break;
                case TMP_Text text:
                    line["text"] = new
                    {
                        words = Short(text.text, 200), font = text.font != null ? text.font.name : null, size = text.fontSize, auto = text.enableAutoSizing,
                        colour = Hex(text.color), mat = text.fontSharedMaterial != null ? text.fontSharedMaterial.name : null, align = text.alignment.ToString(),
                        style = text.fontStyle.ToString(), spacing = text.characterSpacing, gradient = text.enableVertexGradient, ray = text.raycastTarget
                    };
                    break;
                case Selectable selectable:
                    var colours = selectable.colors;
                    var sprites = selectable.spriteState;
                    line["selectable"] = new
                    {
                        type = type.Name, on = selectable.interactable, transition = selectable.transition.ToString(),
                        target = selectable.targetGraphic != null ? selectable.targetGraphic.name : null,
                        normal = Hex(colours.normalColor), highlighted = Hex(colours.highlightedColor), pressed = Hex(colours.pressedColor), selected = Hex(colours.selectedColor), disabled = Hex(colours.disabledColor),
                        sHighlighted = sprites.highlightedSprite != null ? sprites.highlightedSprite.name : null, sPressed = sprites.pressedSprite != null ? sprites.pressedSprite.name : null,
                        sSelected = sprites.selectedSprite != null ? sprites.selectedSprite.name : null, sDisabled = sprites.disabledSprite != null ? sprites.disabledSprite.name : null
                    };
                    break;
                case HorizontalOrVerticalLayoutGroup group:
                    line["layout"] = new
                    {
                        type = type.Name, spacing = group.spacing, pad = new[] { group.padding.left, group.padding.right, group.padding.top, group.padding.bottom }, align = group.childAlignment.ToString(),
                        control = new[] { group.childControlWidth, group.childControlHeight }, expand = new[] { group.childForceExpandWidth, group.childForceExpandHeight }
                    };
                    break;
                case GridLayoutGroup grid:
                    line["grid"] = new
                    {
                        cell = new[] { grid.cellSize.x, grid.cellSize.y }, spacing = new[] { grid.spacing.x, grid.spacing.y }, pad = new[] { grid.padding.left, grid.padding.right, grid.padding.top, grid.padding.bottom },
                        align = grid.childAlignment.ToString(), constraint = grid.constraint.ToString(), count = grid.constraintCount, corner = grid.startCorner.ToString(), axis = grid.startAxis.ToString()
                    };
                    break;
                case LayoutElement element:
                    line["element"] = new { min = new[] { element.minWidth, element.minHeight }, pref = new[] { element.preferredWidth, element.preferredHeight }, flex = new[] { element.flexibleWidth, element.flexibleHeight }, ignore = element.ignoreLayout };
                    break;
                case ContentSizeFitter fitter:
                    line["fitter"] = new { h = fitter.horizontalFit.ToString(), v = fitter.verticalFit.ToString() };
                    break;
                case CanvasGroup canvasGroup:
                    line["group"] = new { alpha = canvasGroup.alpha, on = canvasGroup.interactable, rays = canvasGroup.blocksRaycasts };
                    break;
                case Canvas canvas:
                    line["canvas"] = new { mode = canvas.renderMode.ToString(), order = canvas.sortingOrder, over = canvas.overrideSorting, layer = canvas.sortingLayerName, scale = canvas.scaleFactor };
                    break;
                case SpriteRenderer renderer:
                    line["spriteRenderer"] = new { sprite = renderer.sprite != null ? renderer.sprite.name : null, colour = Hex(renderer.color), mat = renderer.sharedMaterial != null ? renderer.sharedMaterial.name : null };
                    break;
            }
            // the game's own components: their colours, sprites and materials are fields of the prefab
            if (!(component is MonoBehaviour) || type.Namespace == null || type.Namespace.StartsWith("UnityEngine", StringComparison.Ordinal) || type.Namespace.StartsWith("TMPro", StringComparison.Ordinal)) return;
            var fields = new Dictionary<string, object>();
            for (var of = type; of != null && of != typeof(MonoBehaviour); of = of.BaseType)
                foreach (var field in of.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    object value;
                    try { value = field.GetValue(component); }
                    catch (Exception) { continue; }
                    if (value is Color colour) fields[field.Name] = Hex(colour);
                    else if (value is Color32 colour32) fields[field.Name] = Hex(colour32);
                    else if (value is Sprite sprite) fields[field.Name] = sprite != null ? "sprite:" + sprite.name : null;
                    else if (value is Material material) fields[field.Name] = material != null ? "mat:" + material.name : null;
                    else if (value is string words && field.Name.StartsWith("m_", StringComparison.Ordinal)) fields[field.Name] = Short(words, 80);
                    else if (value is GameObject go) fields[field.Name] = go != null ? "go:" + go.name : null;
                    else if (value is Component other) fields[field.Name] = other != null ? other.GetType().Name + ":" + other.name : null;
                    else if (value is bool || value is int || value is float || value is Enum) fields[field.Name] = value.ToString();
                }
            if (fields.Count > 0) line["f." + type.Name] = fields;
        }

        private static object Blue(string file)
        {
            var found = new List<object>();
            foreach (var graphic in UnityEngine.Object.FindObjectsOfType<Graphic>())
            {
                if (graphic == null || !graphic.isActiveAndEnabled || graphic.canvasRenderer.GetInheritedAlpha() <= 0.02f) continue;
                var colour = graphic.color;
                var tagged = graphic is TMP_Text text ? BlueTags(text.text) : null;
                if (!IsBlue(colour) && tagged == null) continue;
                var image = graphic as Image;
                found.Add(new
                {
                    path = AgentBridge.PathOf(graphic.transform), kind = graphic.GetType().Name, colour = Hex(colour), tags = tagged,
                    sprite = image != null && image.sprite != null ? image.sprite.name : null,
                    words = graphic is TMP_Text t ? Short(t.text, 160) : null,
                    rect = graphic.rectTransform != null ? ScreenRect(graphic.rectTransform) : null
                });
            }
            if (!string.IsNullOrEmpty(file)) File.WriteAllText(file, JsonConvert.SerializeObject(found, Formatting.Indented), new UTF8Encoding(false));
            return new { count = found.Count, file, found = found.Count <= 60 ? found : null };
        }

        /// <summary>A colour that reads as blue: blue well over red, and not near black or grey.</summary>
        public static bool IsBlue(Color colour)
        {
            return colour.a > 0.05f && colour.b > 0.18f && colour.b > colour.r * 1.3f && colour.b >= colour.g * 0.98f;
        }

        private static string BlueTags(string words)
        {
            if (string.IsNullOrEmpty(words)) return null;
            string found = null;
            var at = 0;
            while ((at = words.IndexOf("<color=#", at, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                var end = words.IndexOf('>', at);
                if (end < 0) break;
                var hex = words.Substring(at + 7, end - at - 7);
                if (ColorUtility.TryParseHtmlString(hex, out var colour) && IsBlue(colour) && (found == null || found.IndexOf(hex, StringComparison.OrdinalIgnoreCase) < 0))
                    found = found == null ? hex : found + " " + hex;
                at = end;
            }
            return found;
        }

        private static int[] ScreenRect(RectTransform rect)
        {
            var canvas = rect.GetComponentInParent<Canvas>();
            var camera = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            var a = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            var b = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            // left, top (from the top of the screen), width, height
            return new[] { Mathf.RoundToInt(Mathf.Min(a.x, b.x)), Mathf.RoundToInt(Screen.height - Mathf.Max(a.y, b.y)), Mathf.RoundToInt(Mathf.Abs(b.x - a.x)), Mathf.RoundToInt(Mathf.Abs(b.y - a.y)) };
        }

        private static float Round(float value) => Mathf.Round(value * 10f) / 10f;

        private static string Hex(Color colour) => "#" + ColorUtility.ToHtmlStringRGBA(colour);

        private static string Short(string words, int length)
        {
            if (words == null) return null;
            words = words.Replace("\n", "\\n").Replace("\r", "");
            return words.Length > length ? words.Substring(0, length) + "..." : words;
        }
    }
}
