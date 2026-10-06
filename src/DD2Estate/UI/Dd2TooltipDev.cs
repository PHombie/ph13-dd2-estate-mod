using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using Assets.Code.Locale;
using Assets.Code.UI.Canvases;
using Assets.Code.UI.Tooltips;
using Assets.Code.Utils;
using DD2Estate.Dev;
using DD2Estate.Estate;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD2Estate.UI
{
    /// <summary>
    /// Test commands that read DD2's own UI as it is built: a full account of a piece of its hierarchy (sprites
    /// with their borders, colours, fonts with their materials, layout groups), DD2's tooltip prefab and the
    /// places DD2's tooltips take against the things they speak of.
    /// </summary>
    [EstateModule]
    internal static class Dd2TooltipDev
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private static Canvas _testCanvas;
        private static Dd1Tooltip _testTown;
        private static Dungeon.RaidTooltip _testRaid;
        private static Image _testThing;

        private static void Register()
        {
            // Everything a UI object is made of: {"path":"Root/Child","depth":6}.
            AgentBridge.Register("ui.dump", o =>
            {
                var go = AgentBridge.FindObject((string)o["path"]);
                if (go == null) return "not found";
                var text = new StringBuilder();
                Dump(go.transform, 0, (int?)o["depth"] ?? 6, text);
                return text.ToString();
            });
            // DD2's tooltip: the prefab its pool copies ({"which":"skill"} the skill tooltip's), the canvas it
            // lives on, the colours DD2's texts name in it.
            AgentBridge.Register("tooltips.dd2", o =>
            {
                if (!SingletonMonoBehaviour<TooltipCanvasUiBhv>.HasInstance()) return "no tooltip canvas yet";
                var canvasBhv = SingletonMonoBehaviour<TooltipCanvasUiBhv>.Instance;
                var text = new StringBuilder();
                var root = canvasBhv.GetComponentInParent<Canvas>();
                if (root != null) root = root.rootCanvas;
                text.Append("canvas bhv at ").Append(AgentBridge.PathOf(canvasBhv.transform)).Append('\n');
                if (root != null)
                {
                    text.Append("root canvas ").Append(AgentBridge.PathOf(root.transform)).Append(" mode=").Append(root.renderMode).Append(" order=").Append(root.sortingOrder)
                        .Append(" scale=").Append(F(root.scaleFactor)).Append(" camera=").Append(root.worldCamera != null ? root.worldCamera.name : "none")
                        .Append(" plane=").Append(F(root.planeDistance)).Append(" rect=").Append(V(((RectTransform)root.transform).rect.size)).Append('\n');
                    var scaler = root.GetComponent<CanvasScaler>();
                    if (scaler != null)
                        text.Append("scaler ").Append(scaler.uiScaleMode).Append(" ref=").Append(V(scaler.referenceResolution)).Append(" match=").Append(scaler.screenMatchMode).Append(' ').Append(F(scaler.matchWidthOrHeight))
                            .Append(" refPpu=").Append(F(scaler.referencePixelsPerUnit)).Append('\n');
                }
                text.Append("screen ").Append(Screen.width).Append('x').Append(Screen.height).Append('\n');
                var which = (string)o["which"] ?? "text";
                var field = which == "skill" ? "m_skillTooltipContainer" : which == "upgrade" ? "m_upgradeSkillTooltipContainer" : which == "reward" ? "m_rewardLinkedTooltipContainer"
                    : which == "path" ? "m_pathComparisonSkillTooltipContainer" : "m_tooltipContainer";
                var container = typeof(TooltipCanvasUiBhv).GetField(field, Fields)?.GetValue(canvasBhv) as TooltipCanvasUiBhv.TooltipContainer;
                if (container?.m_pool == null) return text.Append("no pool ").Append(field).ToString();
                text.Append("container ").Append(container.m_containerTransform != null ? AgentBridge.PathOf(container.m_containerTransform) : "none").Append('\n');
                text.Append("prefab:\n");
                Dump(container.m_pool.prefab.transform, 1, (int?)o["depth"] ?? 8, text);
                return text.ToString();
            });
            // The colours DD2's own texts ask its string table for: {"keys":"item_nameline,stat_buff"}.
            AgentBridge.Register("tooltips.colours", o =>
            {
                var keys = ((string)o["keys"] ?? "item_nameline,item_typeline,item_effectline,item_discardline,stat_buff,stat_debuff,set_bonus,path_verbose,positive_quirk,negative_quirk,disease_quirk,curse").Split(',');
                var found = new Dictionary<string, string>();
                foreach (var key in keys) found[key.Trim()] = Singleton<Localization>.Instance.TryGetString(key.Trim());
                return found;
            });
            // Everything shown that hears the pointer come and go, with the place to give ui.hover (screen pixels
            // from the lower left): {"filter":"Party","max":60} (a part of the path).
            AgentBridge.Register("ui.hovers", o =>
            {
                var filter = (string)o["filter"];
                var max = (int?)o["max"] ?? 60;
                var lines = new List<string>();
                var seen = new HashSet<GameObject>();
                foreach (var behaviour in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
                {
                    if (!(behaviour is UnityEngine.EventSystems.IPointerEnterHandler) || !behaviour.isActiveAndEnabled || !seen.Add(behaviour.gameObject)) continue;
                    if (!(behaviour.transform is RectTransform rect)) continue;
                    var path = AgentBridge.PathOf(rect);
                    if (!string.IsNullOrEmpty(filter) && path.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var canvas = behaviour.GetComponentInParent<Canvas>();
                    if (canvas == null) continue;
                    var camera = canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.rootCanvas.worldCamera;
                    var at = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center));
                    lines.Add(V(at) + " " + V(rect.rect.size) + " " + path);
                    if (lines.Count >= max) break;
                }
                lines.Sort(StringComparer.Ordinal);
                return lines;
            });
            // What the mod's tooltips took from DD2's prefab: ground, padding, font, sizes, colours.
            AgentBridge.Register("tooltips.look", o => Dd2TooltipLook.Describe());
            // The mod's tooltip boxes: where each stands on the screen (canvas pixels from the upper left), whether
            // it is inside it, what it says; {"all":true} the hidden ones too.
            AgentBridge.Register("tooltips.state", o => Dd2TooltipBox.Snapshot((bool?)o["all"] != true));
            // A tooltip with any words on a canvas of its own, to look at wrapping and at the screen's edges:
            // {"title":"Dismas","body":"Highwayman","x":1800,"y":1000,"width":300} the hamlet's box with its upper
            // left corner there; {"text":"...","raid":true,"above":true,"centred":true} the dungeon's; {"hide":true}.
            AgentBridge.Register("tooltips.test", o =>
            {
                if (_testCanvas == null)
                {
                    _testCanvas = UiKit.Canvas("DD2Estate.TooltipTest", 31000);
                    var frame = UiKit.Rect("Frame", _testCanvas.transform).Stretch();
                    _testThing = UiKit.Image("Thing", frame, null, new Color(0.55f, 0.1f, 0.1f, 0.9f));
                    _testTown = new Dd1Tooltip(frame, 0f);
                    _testRaid = new Dungeon.RaidTooltip(frame);
                }
                _testTown.Hide(null);
                _testRaid.HideAll();
                _testThing.gameObject.SetActive(false);
                if ((bool?)o["hide"] == true) return "hidden";
                var at = new Vector2((float?)o["x"] ?? 800f, (float?)o["y"] ?? 400f);
                var width = (float?)o["width"] ?? 300f;
                // {"thing":72}: a square of that size is the thing the tooltip speaks of, the corner asked for
                // DD1's 13 pixels right of it ({"left":true}: DD1's box, as wide as "width" allows, left of it).
                object owner = _testCanvas;
                var thing = (float?)o["thing"] ?? 0f;
                if (thing > 0f)
                {
                    var left = (bool?)o["left"] == true;
                    ((RectTransform)_testThing.transform).PlaceTopLeft(left ? new Vector2(at.x + width + 36f + 13f, at.y) : new Vector2(at.x - 13f - thing, at.y), new Vector2(0f, 1f), new Vector2(thing, thing));
                    _testThing.gameObject.SetActive(true);
                    owner = _testThing;
                }
                if ((bool?)o["raid"] == true)
                    _testRaid.Show(owner, (string)o["text"] ?? Dungeon.RaidTooltip.Titled((string)o["title"] ?? "Title", (string)o["body"]), at, width, (bool?)o["above"] == true, (bool?)o["centred"] == true, (string)o["style"]);
                else if ((bool?)o["over"] == true) _testTown.ShowOver(owner, at, (string)o["title"], (string)o["body"] ?? (string)o["text"]);
                else if ((bool?)o["leftOf"] == true) _testTown.ShowLeftOf(owner, at, (string)o["title"], (string)o["body"] ?? (string)o["text"], width);
                else _testTown.ShowAt(owner, at, (string)o["title"], (string)o["body"] ?? (string)o["text"], width);
                return Dd2TooltipBox.Snapshot(true);
            });
            // Where DD2's live tooltips stand against their things: {"filter":"Quirk"} (a part of the path or the type).
            AgentBridge.Register("tooltips.sources", o =>
            {
                var filter = (string)o["filter"];
                var lines = new List<string>();
                foreach (var source in UnityEngine.Object.FindObjectsOfType<TooltipUiBhv>(true))
                {
                    var path = AgentBridge.PathOf(source.transform);
                    var type = source.GetType().Name;
                    if (!string.IsNullOrEmpty(filter) && path.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 && type.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var line = new StringBuilder();
                    line.Append(type).Append(source.isActiveAndEnabled ? "" : " (off)").Append(' ').Append(path);
                    var canvas = source.GetComponentInParent<Canvas>();
                    if (canvas != null && source.transform is RectTransform rect)
                    {
                        var camera = canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.rootCanvas.worldCamera;
                        line.Append(" at=").Append(V(RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center)))).Append(" size=").Append(V(rect.rect.size));
                    }
                    line.Append(" |");
                    foreach (var f in typeof(TooltipUiBhv).GetFields(Fields))
                        if (f.FieldType == typeof(float) || f.FieldType == typeof(bool) || f.FieldType.IsEnum) line.Append(' ').Append(f.Name.Replace("m_", "")).Append('=').Append(Short(f.GetValue(source)));
                    lines.Add(line.ToString());
                    if (lines.Count >= ((int?)o["max"] ?? 80)) break;
                }
                return lines;
            });
        }

        private static void Dump(Transform t, int level, int depth, StringBuilder text)
        {
            var pad = new string(' ', level * 2);
            text.Append(pad).Append(t.gameObject.activeSelf ? "" : "(off) ").Append(t.name);
            if (t is RectTransform rt)
                text.Append("  anchors ").Append(V(rt.anchorMin)).Append('-').Append(V(rt.anchorMax)).Append(" pivot ").Append(V(rt.pivot)).Append(" pos ").Append(V(rt.anchoredPosition))
                    .Append(" sizeDelta ").Append(V(rt.sizeDelta)).Append(" rect ").Append(V(rt.rect.size)).Append(" scale ").Append(V(rt.localScale));
            text.Append('\n');
            foreach (var component in t.GetComponents<Component>())
            {
                if (component == null || component is Transform || component is CanvasRenderer) continue;
                text.Append(pad).Append("  * ").Append(component.GetType().Name);
                if (component is Behaviour behaviour && !behaviour.enabled) text.Append(" (disabled)");
                text.Append(": ");
                try { Describe(component, text); }
                catch (Exception e) { text.Append('!').Append(e.GetType().Name); }
                text.Append('\n');
            }
            if (level >= depth)
            {
                if (t.childCount > 0) text.Append(pad).Append("  ... ").Append(t.childCount).Append(" children\n");
                return;
            }
            for (var i = 0; i < t.childCount; i++) Dump(t.GetChild(i), level + 1, depth, text);
        }

        private static void Describe(Component component, StringBuilder text)
        {
            switch (component)
            {
                case TMP_Text label:
                {
                    text.Append("font=").Append(label.font != null ? label.font.name : "none").Append(" material=").Append(label.fontSharedMaterial != null ? label.fontSharedMaterial.name : "none")
                        .Append(" size=").Append(F(label.fontSize)).Append(" auto=").Append(label.enableAutoSizing).Append(' ').Append(F(label.fontSizeMin)).Append("..").Append(F(label.fontSizeMax))
                        .Append(" colour=").Append(C(label.color)).Append(" style=").Append(label.fontStyle).Append(" weight=").Append(label.fontWeight)
                        .Append(" align=").Append(label.alignment).Append(" margin=").Append(label.margin.ToString("0.##"))
                        .Append(" lineSpacing=").Append(F(label.lineSpacing)).Append(" paragraph=").Append(F(label.paragraphSpacing)).Append(" character=").Append(F(label.characterSpacing)).Append(" word=").Append(F(label.wordSpacing))
                        .Append(" wrap=").Append(label.textWrappingMode).Append(" overflow=").Append(label.overflowMode).Append(" rich=").Append(label.richText).Append(" raycast=").Append(label.raycastTarget)
                        .Append(" textStyle=").Append(label.textStyle != null ? label.textStyle.name : "none")
                        .Append(" preferred=").Append(F(label.preferredWidth)).Append('x').Append(F(label.preferredHeight));
                    var material = label.fontSharedMaterial;
                    if (material != null)
                    {
                        text.Append(" shader=").Append(material.shader != null ? material.shader.name : "none");
                        foreach (var property in new[] { "_FaceColor", "_OutlineColor", "_UnderlayColor" })
                            if (material.HasProperty(property)) text.Append(' ').Append(property).Append('=').Append(C(material.GetColor(property)));
                        foreach (var property in new[] { "_FaceDilate", "_OutlineWidth", "_OutlineSoftness", "_UnderlayOffsetX", "_UnderlayOffsetY", "_UnderlayDilate", "_UnderlaySoftness", "_GradientScale", "_WeightNormal", "_WeightBold", "_ScaleRatioA" })
                            if (material.HasProperty(property)) text.Append(' ').Append(property).Append('=').Append(F(material.GetFloat(property)));
                        text.Append(" keywords=").Append(string.Join("+", material.shaderKeywords));
                    }
                    if (label.font != null)
                    {
                        var face = label.font.faceInfo;
                        text.Append(" face(point=").Append(F(face.pointSize)).Append(" line=").Append(F(face.lineHeight)).Append(" asc=").Append(F(face.ascentLine)).Append(" cap=").Append(F(face.capLine))
                            .Append(" desc=").Append(F(face.descentLine)).Append(" scale=").Append(F(face.scale)).Append(')');
                    }
                    var words = label.text ?? "";
                    text.Append(" text=\"").Append(words.Length > 300 ? words.Substring(0, 300) + "..." : words).Append('"');
                    break;
                }
                case Image image:
                {
                    text.Append("sprite=");
                    Sprite(image.sprite, text);
                    text.Append(" type=").Append(image.type).Append(" fillCentre=").Append(image.fillCenter).Append(" ppuMul=").Append(F(image.pixelsPerUnitMultiplier)).Append(" ppu=").Append(F(image.pixelsPerUnit))
                        .Append(" preserve=").Append(image.preserveAspect).Append(" colour=").Append(C(image.color)).Append(" material=").Append(image.material != null ? image.material.name : "none")
                        .Append(" shader=").Append(image.material != null && image.material.shader != null ? image.material.shader.name : "none").Append(" raycast=").Append(image.raycastTarget);
                    break;
                }
                case RawImage raw:
                    text.Append("texture=").Append(raw.texture != null ? raw.texture.name + " " + raw.texture.width + "x" + raw.texture.height : "none").Append(" colour=").Append(C(raw.color)).Append(" uv=").Append(raw.uvRect);
                    break;
                case HorizontalOrVerticalLayoutGroup group:
                    text.Append("padding l").Append(group.padding.left).Append(" r").Append(group.padding.right).Append(" t").Append(group.padding.top).Append(" b").Append(group.padding.bottom)
                        .Append(" spacing=").Append(F(group.spacing)).Append(" align=").Append(group.childAlignment).Append(" control=").Append(group.childControlWidth).Append('/').Append(group.childControlHeight)
                        .Append(" expand=").Append(group.childForceExpandWidth).Append('/').Append(group.childForceExpandHeight).Append(" scale=").Append(group.childScaleWidth).Append('/').Append(group.childScaleHeight)
                        .Append(" reverse=").Append(group.reverseArrangement);
                    break;
                case ContentSizeFitter fitter:
                    text.Append("horizontal=").Append(fitter.horizontalFit).Append(" vertical=").Append(fitter.verticalFit);
                    Serialized(component, text, typeof(ContentSizeFitter));
                    break;
                case LayoutElement element:
                    text.Append("ignore=").Append(element.ignoreLayout).Append(" min=").Append(F(element.minWidth)).Append('x').Append(F(element.minHeight)).Append(" preferred=").Append(F(element.preferredWidth)).Append('x').Append(F(element.preferredHeight))
                        .Append(" flexible=").Append(F(element.flexibleWidth)).Append('x').Append(F(element.flexibleHeight)).Append(" priority=").Append(element.layoutPriority);
                    break;
                case Canvas canvas:
                    text.Append("mode=").Append(canvas.renderMode).Append(" override=").Append(canvas.overrideSorting).Append(" order=").Append(canvas.sortingOrder).Append(" layer=").Append(canvas.sortingLayerName)
                        .Append(" enabled=").Append(canvas.enabled).Append(" scale=").Append(F(canvas.scaleFactor)).Append(" ppu=").Append(F(canvas.referencePixelsPerUnit));
                    break;
                case CanvasGroup group:
                    text.Append("alpha=").Append(F(group.alpha)).Append(" interactable=").Append(group.interactable).Append(" blocks=").Append(group.blocksRaycasts).Append(" ignoreParent=").Append(group.ignoreParentGroups);
                    break;
                case CanvasScaler scaler:
                    text.Append(scaler.uiScaleMode).Append(" ref=").Append(V(scaler.referenceResolution)).Append(" match=").Append(scaler.screenMatchMode).Append(' ').Append(F(scaler.matchWidthOrHeight));
                    break;
                case Mask mask:
                    text.Append("showGraphic=").Append(mask.showMaskGraphic);
                    break;
                case Shadow shadow:
                    text.Append("colour=").Append(C(shadow.effectColor)).Append(" distance=").Append(V(shadow.effectDistance));
                    break;
                case Animator animator:
                    text.Append("controller=").Append(animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : "none");
                    break;
                default:
                    Serialized(component, text, typeof(MonoBehaviour));
                    break;
            }
        }

        // The fields Unity keeps of a behaviour, down to (not including) a base type.
        private static void Serialized(Component component, StringBuilder text, Type stopAt)
        {
            for (var type = component.GetType(); type != null && type != stopAt && type != typeof(Behaviour) && type != typeof(Component); type = type.BaseType)
                foreach (var field in type.GetFields(Fields))
                {
                    if (!field.IsPublic && field.GetCustomAttribute<SerializeField>() == null) continue;
                    text.Append(' ').Append(field.Name).Append('=').Append(Short(field.GetValue(component)));
                }
        }

        private static void Sprite(Sprite sprite, StringBuilder text)
        {
            if (sprite == null)
            {
                text.Append("none");
                return;
            }
            text.Append(sprite.name).Append(" rect=").Append(V(sprite.rect.size)).Append(" border(l,b,r,t)=").Append(sprite.border.ToString("0.##")).Append(" ppu=").Append(F(sprite.pixelsPerUnit))
                .Append(" pivot=").Append(V(sprite.pivot)).Append(" packed=").Append(sprite.packed).Append(" texture=").Append(sprite.texture != null ? sprite.texture.name + " " + sprite.texture.width + "x" + sprite.texture.height : "none");
        }

        private static string Short(object value)
        {
            switch (value)
            {
                case null: return "null";
                case float number: return F(number);
                case Vector2 vector: return V(vector);
                case Color colour: return C(colour);
                case Sprite sprite:
                {
                    var text = new StringBuilder();
                    Sprite(sprite, text);
                    return "{" + text + "}";
                }
                case UnityEngine.Object thing: return thing == null ? "null" : thing.GetType().Name + ":" + thing.name;
                case string words: return "\"" + words + "\"";
                case IList list:
                {
                    var items = new List<string>();
                    foreach (var item in list)
                    {
                        if (items.Count >= 8) { items.Add("..."); break; }
                        items.Add(Short(item));
                    }
                    return "[" + string.Join(", ", items) + "]";
                }
                default:
                {
                    var words = Convert.ToString(value, CultureInfo.InvariantCulture);
                    return words.Length > 80 ? words.Substring(0, 80) + "..." : words;
                }
            }
        }

        private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        private static string V(Vector2 value) => "(" + F(value.x) + "," + F(value.y) + ")";

        private static string C(Color value) => "#" + ColorUtility.ToHtmlStringRGBA(value);
    }
}
