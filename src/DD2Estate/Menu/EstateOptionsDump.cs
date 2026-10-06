using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Assets.Code.UI.Managers;
using Assets.Code.UI.Screens;
using Assets.Code.Utils;
using DD2Estate.Dev;
using DD2Estate.Estate;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DD2Estate.Menu
{
    /// <summary>
    /// Dev bridge: DD2's options screen as it stands in the running game, written to a file, to copy its parts
    /// from what the game has and not from memory.
    ///
    ///   python tools/bridge.py run options.dump file=D:/x/options.txt                  the options screen that is up
    ///   python tools/bridge.py run options.dump what=toggle file=D:/x/toggle.txt       the row its pool copies for a switch
    ///   python tools/bridge.py run options.dump what=slider file=D:/x/slider.txt       ... for a slider
    ///   python tools/bridge.py run options.dump what=input file=D:/x/input.txt         the game's dialog that asks for a text
    ///   python tools/bridge.py run options.dump what=pause file=D:/x/pause.txt         the pause menu that is up
    ///   python tools/bridge.py run options.dump path=Root/Child file=D:/x/part.txt     any object of a scene
    ///
    /// A line an object (size, place, anchors, on or off), a line a part. Unlike ui.dump this one says what
    /// Unity's own controls are made of: a Selectable's transition, colours and navigation, what a Button, a
    /// Toggle, a Slider, a dropdown or an input field calls when it is used (the listeners saved with the
    /// prefab, with the object they are handed), an EventTrigger's entries, a ScrollRect's parts.
    /// </summary>
    [EstateModule]
    internal static class EstateOptionsDump
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static void Register()
        {
            AgentBridge.Register("options.dump", o =>
            {
                var file = (string)o["file"];
                if (string.IsNullOrEmpty(file)) return "give a file to write";
                var what = (string)o["what"] ?? "screen";
                Transform root = null;
                if (o["path"] != null)
                {
                    var found = AgentBridge.FindObject((string)o["path"]);
                    if (found == null) return "not found: " + (string)o["path"];
                    root = found.transform;
                }
                else if (what == "screen")
                {
                    var screen = UnityEngine.Object.FindObjectOfType<OptionsMenuUiBhv>();
                    if (screen == null) return "the options screen is not up";
                    root = screen.transform;
                }
                else if (what == "toggle" || what == "slider")
                {
                    var screen = UnityEngine.Object.FindObjectOfType<OptionsMenuUiBhv>();
                    if (screen == null) return "the options screen is not up";
                    var pool = Traverse.Create(screen).Field(what == "toggle" ? "m_optionsTogglePool" : "m_optionsSliderPool").GetValue() as Component;
                    if (pool == null) return "the screen has no such pool";
                    var prefab = Traverse.Create(pool).Field("prefab").GetValue() as GameObject ?? Traverse.Create(pool).Field("m_prefab").GetValue() as GameObject;
                    if (prefab == null) return "the pool keeps its prefab elsewhere: " + string.Join(", ", FieldNames(pool));
                    root = prefab.transform;
                }
                else if (what == "input" || what == "pause" || what == "options" || what == "confirm")
                {
                    if (!SingletonMonoBehaviour<CommonUiBhv>.HasInstance()) return "no common UI yet";
                    var field = what == "input" ? "m_stringInputDialogPrefab" : what == "pause" ? "m_pauseMenuPrefab" : what == "confirm" ? "m_confirmationDialogPrefab" : "m_optionsMenuPrefab";
                    var prefab = Traverse.Create(SingletonMonoBehaviour<CommonUiBhv>.Instance).Field(field).GetValue() as GameObject;
                    if (prefab == null) return "the common UI has no " + field;
                    root = prefab.transform;
                    if (what == "pause" && (bool?)o["live"] != false)
                    {
                        var live = SingletonMonoBehaviour<ScreenStackBhv>.Instance.FindInstance(prefab, true);
                        if (live != null) root = live.transform;
                    }
                }
                if (root == null) return "what=screen|toggle|slider|input|pause|options|confirm, or a path";
                var text = new StringBuilder();
                text.Append("# ").Append(AgentBridge.PathOf(root)).Append(root.gameObject.scene.IsValid() ? "" : "  (a prefab)").Append("  screen ").Append(Screen.width).Append('x').Append(Screen.height).Append('\n');
                for (var t = root.parent; t != null; t = t.parent)
                {
                    text.Append("#   in ").Append(t.name);
                    if (t is RectTransform r) text.Append(" size ").Append(V(r.rect.size));
                    text.Append(" scale ").Append(V((Vector2)t.localScale));
                    foreach (var c in t.GetComponents<Component>())
                        if (c is Canvas || c is CanvasScaler || c is CanvasGroup || c is GraphicRaycaster)
                        {
                            text.Append(" | ");
                            Part(c, text);
                        }
                    text.Append('\n');
                }
                var count = Dump(root, 0, (int?)o["depth"] ?? 99, text);
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllText(file, text.ToString());
                return new { file, objects = count, root = AgentBridge.PathOf(root) };
            });
        }

        private static IEnumerable<string> FieldNames(object target)
        {
            for (var type = target.GetType(); type != null && type != typeof(MonoBehaviour); type = type.BaseType)
                foreach (var field in type.GetFields(Any | BindingFlags.DeclaredOnly)) yield return field.FieldType.Name + " " + field.Name;
        }

        private static string N(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        private static string V(Vector2 v) => N(v.x) + "," + N(v.y);
        private static string C(Color c) => "#" + ColorUtility.ToHtmlStringRGBA(c);
        private static string Name(UnityEngine.Object thing) => thing == null ? "-" : thing.name;

        private static int Dump(Transform t, int level, int depth, StringBuilder text)
        {
            var pad = new string(' ', level * 2);
            text.Append(pad).Append(t.name).Append(t.gameObject.activeSelf ? "" : "  (OFF)");
            if (t is RectTransform rect)
            {
                text.Append("  size ").Append(V(rect.rect.size)).Append(" at ").Append(V(rect.anchoredPosition)).Append(" anchors ").Append(V(rect.anchorMin)).Append("..").Append(V(rect.anchorMax))
                    .Append(" pivot ").Append(V(rect.pivot)).Append(" delta ").Append(V(rect.sizeDelta));
                if (rect.gameObject.scene.IsValid() && rect.gameObject.activeInHierarchy)
                {
                    var corners = new Vector3[4];
                    rect.GetWorldCorners(corners);
                    var canvas = rect.GetComponentInParent<Canvas>();
                    var camera = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
                    var a = RectTransformUtility.WorldToScreenPoint(camera, corners[1]);
                    var b = RectTransformUtility.WorldToScreenPoint(camera, corners[3]);
                    text.Append(" screen ").Append(N(a.x)).Append(",").Append(N(Screen.height - a.y)).Append(" .. ").Append(N(b.x)).Append(",").Append(N(Screen.height - b.y));
                }
            }
            if (t.localScale != Vector3.one) text.Append(" scale ").Append(V((Vector2)t.localScale));
            text.Append('\n');
            foreach (var component in t.GetComponents<Component>())
            {
                if (component == null || component is Transform || component is CanvasRenderer) continue;
                text.Append(pad).Append("  - ");
                try { Part(component, text); }
                catch (Exception e) { text.Append(component.GetType().Name).Append(" !").Append(e.GetType().Name).Append(' ').Append(e.Message); }
                text.Append('\n');
            }
            var count = 1;
            if (level >= depth)
            {
                if (t.childCount > 0) text.Append(pad).Append("  ... ").Append(t.childCount).Append(" children\n");
                return count;
            }
            for (var i = 0; i < t.childCount; i++) count += Dump(t.GetChild(i), level + 1, depth, text);
            return count;
        }

        private static string Mat(Material material) => material == null ? "-" : material.name + " <" + (material.shader != null ? material.shader.name : "?") + ">";

        private static void Part(Component component, StringBuilder text)
        {
            var type = component.GetType();
            text.Append(type.Name);
            if (component is Behaviour behaviour && !behaviour.enabled) text.Append(" (disabled)");
            switch (component)
            {
                case Image image:
                    text.Append(" sprite ").Append(image.sprite != null ? image.sprite.name + " " + V(image.sprite.rect.size) + " tex " + Name(image.sprite.texture) + " border " + image.sprite.border : "-").Append(" ").Append(image.type);
                    if (image.type == Image.Type.Filled) text.Append(" ").Append(image.fillMethod).Append(" fill ").Append(N(image.fillAmount));
                    if (image.preserveAspect) text.Append(" aspect");
                    text.Append(" colour ").Append(C(image.color)).Append(" mat ").Append(Mat(image.material)).Append(image.raycastTarget ? " raycast" : "").Append(" ppu ").Append(N(image.pixelsPerUnitMultiplier));
                    break;
                case RawImage raw:
                    text.Append(" texture ").Append(raw.texture != null ? raw.texture.name + " " + raw.texture.width + "x" + raw.texture.height : "-").Append(" colour ").Append(C(raw.color)).Append(" mat ").Append(Mat(raw.material));
                    break;
                case TMP_Text label:
                    text.Append(" \"").Append((label.text ?? "").Replace("\n", "\\n")).Append("\" font ").Append(Name(label.font)).Append(" size ").Append(N(label.fontSize))
                        .Append(label.enableAutoSizing ? " auto " + N(label.fontSizeMin) + ".." + N(label.fontSizeMax) : "").Append(" ").Append(label.alignment).Append(" colour ").Append(C(label.color))
                        .Append(" mat ").Append(Name(label.fontSharedMaterial)).Append(" style ").Append(label.fontStyle).Append(" wrap ").Append(label.textWrappingMode).Append(" overflow ").Append(label.overflowMode)
                        .Append(" margin ").Append(label.margin).Append(label.raycastTarget ? " raycast" : "").Append(" spacing ").Append(N(label.characterSpacing)).Append(",").Append(N(label.lineSpacing));
                    break;
                case CanvasGroup group:
                    text.Append(" alpha ").Append(N(group.alpha)).Append(group.blocksRaycasts ? " blocks" : "").Append(group.interactable ? " interactable" : "").Append(group.ignoreParentGroups ? " ignoresParents" : "");
                    break;
                case Canvas canvas:
                    text.Append(" ").Append(canvas.renderMode).Append(canvas.overrideSorting ? " override" : "").Append(" order ").Append(canvas.sortingOrder).Append(" layer ").Append(canvas.sortingLayerName).Append(" factor ").Append(N(canvas.scaleFactor))
                        .Append(" camera ").Append(Name(canvas.worldCamera));
                    break;
                case CanvasScaler scaler:
                    text.Append(" ").Append(scaler.uiScaleMode).Append(" ref ").Append(V(scaler.referenceResolution)).Append(" ").Append(scaler.screenMatchMode).Append(" match ").Append(N(scaler.matchWidthOrHeight));
                    break;
                case HorizontalOrVerticalLayoutGroup layout:
                    text.Append(" spacing ").Append(N(layout.spacing)).Append(" padding l").Append(layout.padding.left).Append(" r").Append(layout.padding.right).Append(" t").Append(layout.padding.top).Append(" b").Append(layout.padding.bottom)
                        .Append(" align ").Append(layout.childAlignment).Append(" control ").Append(layout.childControlWidth).Append(",").Append(layout.childControlHeight)
                        .Append(" scale ").Append(layout.childScaleWidth).Append(",").Append(layout.childScaleHeight)
                        .Append(" expand ").Append(layout.childForceExpandWidth).Append(",").Append(layout.childForceExpandHeight).Append(layout.reverseArrangement ? " reversed" : "");
                    break;
                case GridLayoutGroup grid:
                    text.Append(" cell ").Append(V(grid.cellSize)).Append(" spacing ").Append(V(grid.spacing)).Append(" align ").Append(grid.childAlignment).Append(" ").Append(grid.constraint).Append(" ").Append(grid.constraintCount);
                    break;
                case LayoutElement element:
                    text.Append(" min ").Append(N(element.minWidth)).Append(",").Append(N(element.minHeight)).Append(" preferred ").Append(N(element.preferredWidth)).Append(",").Append(N(element.preferredHeight))
                        .Append(" flexible ").Append(N(element.flexibleWidth)).Append(",").Append(N(element.flexibleHeight)).Append(element.ignoreLayout ? " ignored" : "").Append(" priority ").Append(element.layoutPriority);
                    break;
                case ContentSizeFitter fitter:
                    text.Append(" ").Append(fitter.horizontalFit).Append(",").Append(fitter.verticalFit);
                    break;
                case Mask mask:
                    text.Append(mask.showMaskGraphic ? " shows its graphic" : " hides its graphic");
                    break;
                case ScrollRect scroll:
                    text.Append(" content ").Append(Name(scroll.content)).Append(" viewport ").Append(Name(scroll.viewport)).Append(scroll.horizontal ? " horizontal" : "").Append(scroll.vertical ? " vertical" : "")
                        .Append(" ").Append(scroll.movementType).Append(" elasticity ").Append(N(scroll.elasticity)).Append(scroll.inertia ? " inertia " + N(scroll.decelerationRate) : "").Append(" sensitivity ").Append(N(scroll.scrollSensitivity))
                        .Append(" vbar ").Append(Name(scroll.verticalScrollbar)).Append(" ").Append(scroll.verticalScrollbarVisibility).Append(" spacing ").Append(N(scroll.verticalScrollbarSpacing))
                        .Append(" hbar ").Append(Name(scroll.horizontalScrollbar));
                    Calls(" onValueChanged", scroll.onValueChanged, text);
                    break;
                case Animator animator:
                    text.Append(" controller ").Append(Name(animator.runtimeAnimatorController)).Append(" mode ").Append(animator.updateMode);
                    break;
                case EventTrigger trigger:
                    foreach (var entry in trigger.triggers) Calls(" " + entry.eventID, entry.callback, text);
                    break;
                case Selectable selectable:
                    Selectable(selectable, text);
                    break;
            }
            if (component is MonoBehaviour && !(component is Selectable) && Ours(type)) Fields(component, text);
        }

        private static void Selectable(Selectable s, StringBuilder text)
        {
            text.Append(s.interactable ? "" : " not interactable").Append(" transition ").Append(s.transition).Append(" target ").Append(Name(s.targetGraphic));
            if (s.transition == UnityEngine.UI.Selectable.Transition.ColorTint)
            {
                var c = s.colors;
                text.Append(" colours normal ").Append(C(c.normalColor)).Append(" highlighted ").Append(C(c.highlightedColor)).Append(" pressed ").Append(C(c.pressedColor)).Append(" selected ").Append(C(c.selectedColor))
                    .Append(" disabled ").Append(C(c.disabledColor)).Append(" x").Append(N(c.colorMultiplier)).Append(" fade ").Append(N(c.fadeDuration));
            }
            else if (s.transition == UnityEngine.UI.Selectable.Transition.SpriteSwap)
            {
                var st = s.spriteState;
                text.Append(" sprites highlighted ").Append(Name(st.highlightedSprite)).Append(" pressed ").Append(Name(st.pressedSprite)).Append(" selected ").Append(Name(st.selectedSprite)).Append(" disabled ").Append(Name(st.disabledSprite));
            }
            else if (s.transition == UnityEngine.UI.Selectable.Transition.Animation)
            {
                var a = s.animationTriggers;
                text.Append(" triggers ").Append(a.normalTrigger).Append("/").Append(a.highlightedTrigger).Append("/").Append(a.pressedTrigger).Append("/").Append(a.selectedTrigger).Append("/").Append(a.disabledTrigger);
            }
            var nav = s.navigation;
            text.Append(" nav ").Append(nav.mode);
            if (nav.mode == Navigation.Mode.Explicit)
                text.Append(" up ").Append(Name(nav.selectOnUp)).Append(" down ").Append(Name(nav.selectOnDown)).Append(" left ").Append(Name(nav.selectOnLeft)).Append(" right ").Append(Name(nav.selectOnRight));
            switch (s)
            {
                case Button button:
                    Calls(" onClick", button.onClick, text);
                    break;
                case Toggle toggle:
                    text.Append(toggle.isOn ? " ON" : " off").Append(" graphic ").Append(Name(toggle.graphic)).Append(" group ").Append(Name(toggle.group)).Append(" ").Append(toggle.toggleTransition);
                    Calls(" onValueChanged", toggle.onValueChanged, text);
                    break;
                case Slider slider:
                    text.Append(" value ").Append(N(slider.value)).Append(" of ").Append(N(slider.minValue)).Append("..").Append(N(slider.maxValue)).Append(slider.wholeNumbers ? " whole" : "").Append(" ").Append(slider.direction)
                        .Append(" fill ").Append(Name(slider.fillRect)).Append(" handle ").Append(Name(slider.handleRect));
                    Calls(" onValueChanged", slider.onValueChanged, text);
                    break;
                case Scrollbar bar:
                    text.Append(" value ").Append(N(bar.value)).Append(" size ").Append(N(bar.size)).Append(" ").Append(bar.direction).Append(" handle ").Append(Name(bar.handleRect));
                    break;
                case TMP_Dropdown dropdown:
                    text.Append(" value ").Append(dropdown.value).Append(" template ").Append(Name(dropdown.template)).Append(" caption ").Append(Name(dropdown.captionText)).Append(" captionImage ").Append(Name(dropdown.captionImage))
                        .Append(" item ").Append(Name(dropdown.itemText)).Append(" itemImage ").Append(Name(dropdown.itemImage)).Append(" placeholder ").Append(Name(dropdown.placeholder)).Append(" alphaFade ").Append(N(dropdown.alphaFadeSpeed)).Append(" options [");
                    foreach (var option in dropdown.options) text.Append('"').Append(option.text).Append("\" ");
                    text.Append("]");
                    Calls(" onValueChanged", dropdown.onValueChanged, text);
                    break;
                case TMP_InputField input:
                    text.Append(" text \"").Append(input.text).Append("\" textComponent ").Append(Name(input.textComponent)).Append(" viewport ").Append(Name(input.textViewport)).Append(" placeholder ").Append(Name(input.placeholder))
                        .Append(" limit ").Append(input.characterLimit).Append(" ").Append(input.contentType).Append(" ").Append(input.lineType).Append(" caret ").Append(C(input.caretColor)).Append(" w").Append(input.caretWidth)
                        .Append(input.customCaretColor ? " customCaret" : "").Append(" selection ").Append(C(input.selectionColor)).Append(" pointSize ").Append(N(input.pointSize)).Append(" font ").Append(Name(input.fontAsset))
                        .Append(input.onFocusSelectAll ? " selectAllOnFocus" : "").Append(input.resetOnDeActivation ? " resetOnDeactivation" : "").Append(input.restoreOriginalTextOnEscape ? " restoreOnEscape" : "")
                        .Append(input.richText ? " rich" : "").Append(input.readOnly ? " readOnly" : "");
                    Calls(" onValueChanged", input.onValueChanged, text);
                    Calls(" onEndEdit", input.onEndEdit, text);
                    Calls(" onSubmit", input.onSubmit, text);
                    Calls(" onSelect", input.onSelect, text);
                    Calls(" onDeselect", input.onDeselect, text);
                    break;
            }
        }

        // The listeners saved with a prefab: whom they call, what, and the object they hand over.
        private static void Calls(string name, UnityEventBase evt, StringBuilder text)
        {
            if (evt == null) return;
            var count = evt.GetPersistentEventCount();
            if (count == 0) return;
            text.Append(name).Append(" -> ");
            IList calls = null;
            try
            {
                var group = typeof(UnityEventBase).GetField("m_PersistentCalls", Any)?.GetValue(evt);
                calls = group?.GetType().GetField("m_Calls", Any)?.GetValue(group) as IList;
            }
            catch (Exception) { calls = null; }
            for (var i = 0; i < count; i++)
            {
                var target = evt.GetPersistentTarget(i);
                text.Append(i > 0 ? "; " : "").Append(target != null ? target.GetType().Name + ":" + target.name : "null").Append('.').Append(evt.GetPersistentMethodName(i));
                try
                {
                    var call = calls != null && i < calls.Count ? calls[i] : null;
                    var mode = call?.GetType().GetField("m_Mode", Any)?.GetValue(call);
                    var args = call?.GetType().GetField("m_Arguments", Any)?.GetValue(call);
                    if (args != null && mode != null)
                    {
                        var m = mode.ToString();
                        if (m == "Object") text.Append('(').Append(Value(args.GetType().GetField("m_ObjectArgument", Any)?.GetValue(args), 2)).Append(')');
                        else if (m == "Int") text.Append('(').Append(args.GetType().GetField("m_IntArgument", Any)?.GetValue(args)).Append(')');
                        else if (m == "Float") text.Append('(').Append(args.GetType().GetField("m_FloatArgument", Any)?.GetValue(args)).Append(')');
                        else if (m == "String") text.Append("(\"").Append(args.GetType().GetField("m_StringArgument", Any)?.GetValue(args)).Append("\")");
                        else if (m == "Bool") text.Append('(').Append(args.GetType().GetField("m_BoolArgument", Any)?.GetValue(args)).Append(')');
                        else text.Append('<').Append(m).Append('>');
                    }
                    text.Append(' ').Append(evt.GetPersistentListenerState(i) == UnityEventCallState.Off ? "OFF" : "");
                }
                catch (Exception) { }
            }
        }

        private static bool Ours(Type type)
        {
            var space = type.Namespace ?? "";
            return !space.StartsWith("UnityEngine", StringComparison.Ordinal) && !space.StartsWith("TMPro", StringComparison.Ordinal);
        }

        private static void Fields(object target, StringBuilder text)
        {
            for (var type = target.GetType(); type != null && type != typeof(MonoBehaviour) && type != typeof(object); type = type.BaseType)
            {
                if ((type.Namespace ?? "").StartsWith("UnityEngine", StringComparison.Ordinal)) break;
                foreach (var field in type.GetFields(Any | BindingFlags.DeclaredOnly))
                {
                    if (field.IsNotSerialized) continue;
                    if (!field.IsPublic && field.GetCustomAttribute<SerializeField>() == null) continue;
                    object value;
                    try { value = field.GetValue(target); }
                    catch (Exception) { continue; }
                    if (value is UnityEventBase evt)
                    {
                        Calls(" | " + field.Name, evt, text);
                        continue;
                    }
                    text.Append(" | ").Append(field.Name).Append('=').Append(Value(value, 0));
                }
            }
        }

        private static string Value(object value, int level)
        {
            switch (value)
            {
                case null: return "null";
                case string s: return "\"" + (s.Length > 80 ? s.Substring(0, 80) + "..." : s) + "\"";
                case bool b: return b ? "true" : "false";
                case float f: return N(f);
                case Vector2 v2: return "(" + V(v2) + ")";
                case Color colour: return C(colour);
                case Sprite sprite: return sprite == null ? "null" : "Sprite:" + sprite.name;
                case Material material: return material == null ? "null" : "Material:" + Mat(material);
                case Component component: return component == null ? "null" : component.GetType().Name + ":" + component.name;
                case UnityEngine.Object thing: return thing == null ? "null" : thing.GetType().Name + ":" + thing.name;
            }
            var type = value.GetType();
            if (type.IsPrimitive || type.IsEnum) return Convert.ToString(value, CultureInfo.InvariantCulture);
            if (value is IEnumerable list)
            {
                var parts = new List<string>();
                foreach (var item in list)
                {
                    if (parts.Count >= 24)
                    {
                        parts.Add("...");
                        break;
                    }
                    parts.Add(Value(item, level + 1));
                }
                return "[" + string.Join(", ", parts) + "]";
            }
            if (level >= 2) return type.Name;
            var inner = new List<string>();
            foreach (var field in type.GetFields(Any))
            {
                if (inner.Count >= 12) break;
                try { inner.Add(field.Name + "=" + Value(field.GetValue(value), level + 1)); }
                catch (Exception) { inner.Add(field.Name + "=!"); }
            }
            return type.Name + "{" + string.Join(", ", inner) + "}";
        }
    }
}
