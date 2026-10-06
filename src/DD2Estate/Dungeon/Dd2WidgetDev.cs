using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using DD2Estate.Dev;
using DD2Estate.Estate;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Playables;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// Dev bridge: a look into DD2's own widgets, to copy their look from the running game and not from memory.
    ///
    ///   python tools/bridge.py run ui.dump expr=$CombatTorchUiBhv file=D:/x/torch.txt
    ///   python tools/bridge.py run ui.dump path=DD2Estate.DungeonHud/Screen/Dd2Torch file=D:/x/copy.txt
    ///   python tools/bridge.py run ui.asset key=Assets/Prefabs/UI/actor_info_panel.prefab file=D:/x/info.txt
    ///   python tools/bridge.py run input.drag x1=1300 y1=200 x2=1380 y2=230 button=left
    ///
    /// ui.dump writes a widget's whole tree to a file, a line an object and a line a part: where it stands
    /// (anchors, size, its corners on screen in pixels from the top left), whether it is on, its pictures (sprite,
    /// texture, material, shader, colour), sliders, groups, layouts, texts, and what the game's own behaviours on
    /// it hold in their serialized fields. {"all":true} names every object of that type instead of the first.
    /// ui.asset does the same for a prefab the game keeps an address for (it is loaded, written and let go).
    /// input.drag is a real drag of the pointer (needs the window's focus): pressed at one place, moved to
    /// another in steps, let go; {"button":"right"} for the right button, {"hold":0.5} to keep it down at the end.
    ///
    /// The looks taken from DD2's fight (Dd2Hud, Dd2Torch, Dd2Bars):
    ///
    ///   python tools/bridge.py run hud.look                       what is shown, what was asked of the game and what came
    ///   python tools/bridge.py run hud.look torch=dd1 bars=dd1    DD1's torch and bars again (dd2 puts DD2's back)
    ///   python tools/bridge.py run hud.look rise=0                the heroes' bars exactly on the fight's line
    ///   python tools/bridge.py run hud.look pictures=dd1          the marks under the bars all with DD1's little pictures
    ///   python tools/bridge.py run hud.look source=Assets/Prefabs/UI/Combat/BattleInfo/BattleInfo.prefab reset=true
    ///   python tools/bridge.py run ui.png key=Assets/Art/UI/HUD/ui_target_friendly.png file=D:/x/mark.png
    ///   python tools/bridge.py run ui.png expr=@Path/To/Object:Image file=D:/x/sprite.png
    ///   python tools/bridge.py run ui.sprites filter=disease
    ///
    /// ui.png writes a picture of the game's to a file, to look at it (a sprite by its address, or the one an
    /// Image on screen shows); ui.sprites names the sprites the game has loaded.
    /// </summary>
    [EstateModule]
    internal static class Dd2WidgetDev
    {
        private static void Register()
        {
            AgentBridge.Register("ui.dump", o =>
            {
                var file = (string)o["file"];
                if (string.IsNullOrEmpty(file)) return "give a file to write";
                var depth = (int?)o["depth"] ?? 99;
                var fields = (bool?)o["fields"] ?? true;
                var roots = new List<Transform>();
                if (o["path"] != null)
                {
                    var found = AgentBridge.FindObject((string)o["path"]);
                    if (found == null) return "not found: " + (string)o["path"];
                    roots.Add(found.transform);
                }
                else if (o["find"] != null)
                {
                    // {"find":"Trays"}: the first object on that is called so, wherever it hangs
                    var found = GameObject.Find((string)o["find"]);
                    if (found == null) return "nothing on is called " + (string)o["find"];
                    roots.Add(found.transform);
                }
                else if ((bool?)o["all"] == true)
                {
                    var name = ((string)o["expr"] ?? "").TrimStart('$');
                    foreach (var behaviour in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
                        if (behaviour != null && behaviour.GetType().Name == name && behaviour.gameObject.scene.IsValid()) roots.Add(behaviour.transform);
                }
                else
                {
                    var value = Reflect.Eval((string)o["expr"]);
                    var root = value is Component component ? component.transform : value is GameObject go ? go.transform : null;
                    if (root == null) return "that is not an object of a scene";
                    roots.Add(root);
                }
                if (roots.Count == 0) return "nothing to write";
                var text = new StringBuilder();
                var count = 0;
                foreach (var root in roots)
                {
                    text.Append("# ").Append(AgentBridge.PathOf(root)).Append('\n');
                    Parents(root, text);
                    count += Dump(root, 0, depth, fields, text);
                    text.Append('\n');
                }
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllText(file, text.ToString());
                return new { file, objects = count, roots = roots.Count, screen = new[] { Screen.width, Screen.height } };
            });
            AgentBridge.Register("ui.asset", o =>
            {
                var key = (string)o["key"];
                var file = (string)o["file"];
                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(file)) return "give a key and a file";
                var depth = (int?)o["depth"] ?? 99;
                var handle = Addressables.LoadAssetAsync<GameObject>(key);
                handle.Completed += loaded =>
                {
                    var text = new StringBuilder();
                    try
                    {
                        if (loaded.Status != AsyncOperationStatus.Succeeded || loaded.Result == null) text.Append("not loaded: ").Append(loaded.OperationException?.Message);
                        else
                        {
                            text.Append("# asset ").Append(key).Append('\n');
                            Dump(loaded.Result.transform, 0, depth, true, text);
                        }
                    }
                    catch (Exception e) { text.Append("\nfailed: ").Append(e); }
                    Directory.CreateDirectory(Path.GetDirectoryName(file));
                    File.WriteAllText(file, text.ToString());
                    Addressables.Release(loaded);
                };
                return "loading; the file is written when the asset is there";
            });
            AgentBridge.Register("hud.look", o =>
            {
                if (o["torch"] != null) Dd2Torch.Enabled = (string)o["torch"] != "dd1";
                if (o["bars"] != null) Dd2Hud.BarsDd2 = (string)o["bars"] != "dd1";
                if (o["rise"] != null) Dd2Hud.BarsRise = (float)o["rise"];
                if (o["pictures"] != null) RaidTrays.Dd1Pictures = (string)o["pictures"] == "dd1";
                // another picture for a mark, to try it: {"icon":"disease","art":"sprite:icon_disease"}; {"size":26,"plate":false}
                if (o["icon"] != null)
                {
                    var icon = TrayIcons.Find((string)o["icon"]);
                    if (icon == null) return "no such icon";
                    if (o["art"] != null) icon.Dd2Art = string.IsNullOrEmpty((string)o["art"]) ? null : ((string)o["art"]).Split('|');
                    if (o["size"] != null) icon.Dd2Size = Vector2.one * (float)o["size"];
                    if (o["plate"] != null) icon.Dd2Plate = (bool)o["plate"];
                }
                if (o["source"] != null) Dd2Hud.Source = (string)o["source"];
                if ((bool?)o["reset"] == true)
                {
                    Dd2Torch.Reset();
                    Dd2Bars.Reset();
                }
                return new { look = Dd2Hud.Describe(), torch = Dd2Torch.Status, bars = Dd2Bars.Status, torchOnScreen = DungeonHud.Torch?.Describe(), trays = DungeonHud.Trays?.Describe() };
            });
            AgentBridge.Register("ui.png", o =>
            {
                var file = (string)o["file"];
                if (string.IsNullOrEmpty(file)) return "give a file to write";
                if (o["expr"] != null)
                {
                    var value = Reflect.Eval((string)o["expr"]);
                    var sprite = value is Image image ? image.overrideSprite ?? image.sprite : value as Sprite;
                    if (sprite == null) return "no sprite there";
                    return Png(sprite.texture, sprite.textureRect, file, sprite.name + " border " + sprite.border + " ppu " + sprite.pixelsPerUnit);
                }
                if (o["sprite"] != null)
                {
                    foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
                        if (sprite != null && sprite.name == (string)o["sprite"])
                            return Png(sprite.texture, sprite.textureRect, file, sprite.name + " border " + sprite.border + " ppu " + sprite.pixelsPerUnit);
                    return "no such sprite in memory";
                }
                var key = (string)o["key"];
                if (string.IsNullOrEmpty(key)) return "give a key, an expr or a sprite's name";
                // a picture of the game's is a sprite or a bare texture: the one, then the other
                var handle = Addressables.LoadAssetAsync<Sprite>(key);
                handle.Completed += loaded =>
                {
                    try
                    {
                        if (loaded.Status == AsyncOperationStatus.Succeeded && loaded.Result != null)
                        {
                            var sprite = loaded.Result;
                            Png(sprite.texture, sprite.textureRect, file, sprite.name + " border " + sprite.border + " ppu " + sprite.pixelsPerUnit);
                        }
                        else
                        {
                            var bare = Addressables.LoadAssetAsync<Texture2D>(key);
                            bare.Completed += texture =>
                            {
                                try
                                {
                                    if (texture.Status == AsyncOperationStatus.Succeeded && texture.Result != null)
                                        Png(texture.Result, new Rect(0f, 0f, texture.Result.width, texture.Result.height), file, texture.Result.name);
                                    else File.WriteAllText(file + ".txt", "not loaded: " + texture.OperationException?.Message);
                                }
                                catch (Exception e) { File.WriteAllText(file + ".txt", "failed: " + e); }
                                Addressables.Release(texture);
                            };
                        }
                    }
                    catch (Exception e) { File.WriteAllText(file + ".txt", "failed: " + e); }
                    Addressables.Release(loaded);
                };
                return "loading; the file is written when the picture is there";
            });
            AgentBridge.Register("ui.sprites", o =>
            {
                var filter = ((string)o["filter"] ?? "").ToLowerInvariant();
                var names = new SortedDictionary<string, string>();
                foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
                {
                    if (sprite == null || (filter.Length > 0 && sprite.name.ToLowerInvariant().IndexOf(filter, StringComparison.Ordinal) < 0)) continue;
                    names[sprite.name] = N(sprite.rect.width) + "x" + N(sprite.rect.height) + (sprite.texture != null ? " in " + sprite.texture.name : "");
                    if (names.Count >= 300) break;
                }
                return names;
            });
            // The game's window at another size, to look at a screen at it: {"width":1706,"height":960}; {"restore":true}
            // puts back what the game had when this was first used (do that before the game is closed: the size is kept).
            AgentBridge.Register("ui.resolution", o =>
            {
                if (_resolutionWas == null) _resolutionWas = new[] { Screen.width, Screen.height, (int)Screen.fullScreenMode };
                if ((bool?)o["restore"] == true) Screen.SetResolution(_resolutionWas[0], _resolutionWas[1], (FullScreenMode)_resolutionWas[2]);
                else if (o["width"] != null && o["height"] != null) Screen.SetResolution((int)o["width"], (int)o["height"], FullScreenMode.Windowed);
                return new { now = new[] { Screen.width, Screen.height }, mode = Screen.fullScreenMode.ToString(), was = _resolutionWas };
            });
            AgentBridge.Register("input.drag", o =>
            {
                var from = new Vector2((float)o["x1"], (float)o["y1"]);
                var to = new Vector2((float)o["x2"], (float)o["y2"]);
                var right = ((string)o["button"] ?? "left") == "right";
                Plugin.Host.StartCoroutine(Drag(from, to, right ? MouseButton.Right : MouseButton.Left, (int?)o["steps"] ?? 8, (float?)o["hold"] ?? 0f));
                return new { focused = Application.isFocused };
            });
        }

        private static int[] _resolutionWas;

        // A picture of the game's as a PNG: its texture cannot be read, so it is drawn into one that can.
        private static object Png(Texture texture, Rect area, string file, string what)
        {
            if (texture == null) return "the picture has no texture";
            var target = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var before = RenderTexture.active;
            Texture2D copy = null;
            try
            {
                Graphics.Blit(texture, target);
                RenderTexture.active = target;
                copy = new Texture2D(Mathf.Max(1, (int)area.width), Mathf.Max(1, (int)area.height), TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(area.x, area.y, copy.width, copy.height), 0, 0);
                copy.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllBytes(file, copy.EncodeToPNG());
                File.WriteAllText(file + ".txt", what + "\n" + copy.width + "x" + copy.height + " of " + texture.name + " " + texture.width + "x" + texture.height);
                return new { file, what, size = new[] { copy.width, copy.height }, texture = texture.name };
            }
            finally
            {
                RenderTexture.active = before;
                RenderTexture.ReleaseTemporary(target);
                if (copy != null) UnityEngine.Object.Destroy(copy);
            }
        }

        private static IEnumerator Drag(Vector2 from, Vector2 to, MouseButton button, int steps, float hold)
        {
            var mouse = Mouse.current;
            if (mouse == null) yield break;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = from });
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = from }.WithButton(button));
            yield return null;
            yield return null;
            for (var i = 1; i <= steps; i++)
            {
                var at = Vector2.Lerp(from, to, i / (float)steps);
                InputSystem.QueueStateEvent(mouse, new MouseState { position = at, delta = (to - from) / steps }.WithButton(button));
                yield return null;
                yield return null;
            }
            if (hold > 0f) yield return new WaitForSecondsRealtime(hold);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = to });
        }

        // ---- the tree ----------------------------------------------------------------------------------

        private static string N(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        private static string V(Vector2 v) => N(v.x) + "," + N(v.y);
        private static string V(Vector3 v) => N(v.x) + "," + N(v.y) + "," + N(v.z);
        private static string C(Color c) => "#" + ColorUtility.ToHtmlStringRGBA(c);

        // What the widget hangs in: every parent up to the scene's root, with what scales and fades it.
        private static void Parents(Transform root, StringBuilder text)
        {
            for (var t = root.parent; t != null; t = t.parent)
            {
                text.Append("#   in ").Append(t.name);
                var rect = t as RectTransform;
                if (rect != null) text.Append(" rect ").Append(V(rect.rect.size)).Append(" at ").Append(V(rect.anchoredPosition)).Append(" anchors ").Append(V(rect.anchorMin)).Append("..").Append(V(rect.anchorMax)).Append(" pivot ").Append(V(rect.pivot));
                text.Append(" scale ").Append(V(t.localScale));
                var canvas = t.GetComponent<Canvas>();
                if (canvas != null) text.Append(" | Canvas ").Append(canvas.renderMode).Append(" order ").Append(canvas.sortingOrder).Append(" layer ").Append(canvas.sortingLayerName).Append(" factor ").Append(N(canvas.scaleFactor)).Append(" camera ").Append(canvas.worldCamera != null ? canvas.worldCamera.name : "-").Append(" plane ").Append(N(canvas.planeDistance));
                var scaler = t.GetComponent<CanvasScaler>();
                if (scaler != null) text.Append(" | Scaler ").Append(scaler.uiScaleMode).Append(" ref ").Append(V(scaler.referenceResolution)).Append(" ").Append(scaler.screenMatchMode).Append(" match ").Append(N(scaler.matchWidthOrHeight));
                var group = t.GetComponent<CanvasGroup>();
                if (group != null) text.Append(" | Group alpha ").Append(N(group.alpha));
                text.Append('\n');
            }
        }

        private static int Dump(Transform t, int level, int depth, bool fields, StringBuilder text)
        {
            var pad = new string(' ', level * 2);
            text.Append(pad).Append(t.name).Append(t.gameObject.activeSelf ? "" : "  (OFF)");
            var rect = t as RectTransform;
            if (rect != null)
            {
                text.Append("  size ").Append(V(rect.rect.size)).Append(" at ").Append(V(rect.anchoredPosition)).Append(" anchors ").Append(V(rect.anchorMin)).Append("..").Append(V(rect.anchorMax))
                    .Append(" pivot ").Append(V(rect.pivot));
                if (rect.gameObject.scene.IsValid())
                {
                    var corners = new Vector3[4];
                    rect.GetWorldCorners(corners);
                    var canvas = rect.GetComponentInParent<Canvas>();
                    var camera = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
                    var a = RectTransformUtility.WorldToScreenPoint(camera, corners[1]);        // top left
                    var b = RectTransformUtility.WorldToScreenPoint(camera, corners[3]);        // bottom right
                    text.Append(" screen ").Append(N(a.x)).Append(",").Append(N(Screen.height - a.y)).Append(" .. ").Append(N(b.x)).Append(",").Append(N(Screen.height - b.y));
                }
            }
            else text.Append("  pos ").Append(V(t.localPosition));
            if (t.localScale != Vector3.one) text.Append(" scale ").Append(V(t.localScale));
            if (t.localRotation != Quaternion.identity) text.Append(" rot ").Append(V(t.localEulerAngles));
            text.Append('\n');

            foreach (var component in t.GetComponents<Component>())
            {
                if (component == null || component is Transform || component is CanvasRenderer) continue;
                text.Append(pad).Append("  - ");
                try { Part(component, fields, text); }
                catch (Exception e) { text.Append(component.GetType().Name).Append(" !").Append(e.GetType().Name); }
                text.Append('\n');
            }
            var count = 1;
            if (level >= depth)
            {
                if (t.childCount > 0) text.Append(pad).Append("  ... ").Append(t.childCount).Append(" children\n");
                return count;
            }
            for (var i = 0; i < t.childCount; i++) count += Dump(t.GetChild(i), level + 1, depth, fields, text);
            return count;
        }

        private static string Mat(Material material)
        {
            if (material == null) return "-";
            return material.name + " <" + (material.shader != null ? material.shader.name : "?") + ">";
        }

        private static void Part(Component component, bool fields, StringBuilder text)
        {
            var type = component.GetType();
            text.Append(type.Name);
            if (component is Behaviour behaviour && !behaviour.enabled) text.Append(" (disabled)");
            switch (component)
            {
                case Image image:
                    text.Append(" sprite ").Append(image.sprite != null ? image.sprite.name + " " + V(image.sprite.rect.size) + " tex " + (image.sprite.texture != null ? image.sprite.texture.name : "-") + " border " + image.sprite.border : "-")
                        .Append(" ").Append(image.type);
                    if (image.type == Image.Type.Filled) text.Append(" ").Append(image.fillMethod).Append(" origin ").Append(image.fillOrigin).Append(" fill ").Append(N(image.fillAmount));
                    if (image.preserveAspect) text.Append(" aspect");
                    text.Append(" colour ").Append(C(image.color)).Append(" mat ").Append(Mat(image.material)).Append(image.raycastTarget ? " raycast" : "").Append(image.maskable ? "" : " unmaskable")
                        .Append(" ppu ").Append(N(image.pixelsPerUnitMultiplier));
                    break;
                case RawImage raw:
                    text.Append(" texture ").Append(raw.texture != null ? raw.texture.name + " " + raw.texture.width + "x" + raw.texture.height : "-").Append(" uv ").Append(raw.uvRect).Append(" colour ").Append(C(raw.color)).Append(" mat ").Append(Mat(raw.material));
                    break;
                case TMP_Text label:
                    text.Append(" \"").Append((label.text ?? "").Replace("\n", "\\n")).Append("\" font ").Append(label.font != null ? label.font.name : "-").Append(" size ").Append(N(label.fontSize))
                        .Append(label.enableAutoSizing ? " auto " + N(label.fontSizeMin) + ".." + N(label.fontSizeMax) : "").Append(" ").Append(label.alignment).Append(" colour ").Append(C(label.color))
                        .Append(" mat ").Append(label.fontSharedMaterial != null ? label.fontSharedMaterial.name : "-").Append(" style ").Append(label.fontStyle);
                    break;
                case Slider slider:
                    text.Append(" value ").Append(N(slider.value)).Append(" of ").Append(N(slider.minValue)).Append("..").Append(N(slider.maxValue)).Append(" ").Append(slider.direction)
                        .Append(" fill ").Append(slider.fillRect != null ? slider.fillRect.name : "-").Append(" handle ").Append(slider.handleRect != null ? slider.handleRect.name : "-").Append(slider.interactable ? "" : " not interactable");
                    break;
                case CanvasGroup group:
                    text.Append(" alpha ").Append(N(group.alpha)).Append(group.blocksRaycasts ? " blocks" : "").Append(group.interactable ? " interactable" : "").Append(group.ignoreParentGroups ? " ignoresParents" : "");
                    break;
                case Canvas canvas:
                    text.Append(" ").Append(canvas.renderMode).Append(canvas.overrideSorting ? " override" : "").Append(" order ").Append(canvas.sortingOrder).Append(" layer ").Append(canvas.sortingLayerName).Append(" factor ").Append(N(canvas.scaleFactor));
                    break;
                case CanvasScaler scaler:
                    text.Append(" ").Append(scaler.uiScaleMode).Append(" ref ").Append(V(scaler.referenceResolution)).Append(" ").Append(scaler.screenMatchMode).Append(" match ").Append(N(scaler.matchWidthOrHeight));
                    break;
                case HorizontalOrVerticalLayoutGroup layout:
                    text.Append(" spacing ").Append(N(layout.spacing)).Append(" padding ").Append(layout.padding.left).Append(",").Append(layout.padding.right).Append(",").Append(layout.padding.top).Append(",").Append(layout.padding.bottom)
                        .Append(" align ").Append(layout.childAlignment).Append(" control ").Append(layout.childControlWidth).Append(",").Append(layout.childControlHeight)
                        .Append(" expand ").Append(layout.childForceExpandWidth).Append(",").Append(layout.childForceExpandHeight).Append(layout.reverseArrangement ? " reversed" : "");
                    break;
                case GridLayoutGroup grid:
                    text.Append(" cell ").Append(V(grid.cellSize)).Append(" spacing ").Append(V(grid.spacing)).Append(" align ").Append(grid.childAlignment).Append(" ").Append(grid.constraint).Append(" ").Append(grid.constraintCount)
                        .Append(" start ").Append(grid.startCorner).Append(" axis ").Append(grid.startAxis);
                    break;
                case LayoutElement element:
                    text.Append(" min ").Append(N(element.minWidth)).Append(",").Append(N(element.minHeight)).Append(" preferred ").Append(N(element.preferredWidth)).Append(",").Append(N(element.preferredHeight))
                        .Append(" flexible ").Append(N(element.flexibleWidth)).Append(",").Append(N(element.flexibleHeight)).Append(element.ignoreLayout ? " ignored" : "");
                    break;
                case ContentSizeFitter fitter:
                    text.Append(" ").Append(fitter.horizontalFit).Append(",").Append(fitter.verticalFit);
                    break;
                case Mask mask:
                    text.Append(mask.showMaskGraphic ? " shows its graphic" : " hides its graphic");
                    break;
                case PlayableDirector director:
                    text.Append(" asset ").Append(director.playableAsset != null ? director.playableAsset.name : "-").Append(" ").Append(director.state).Append(" time ").Append(N((float)director.time)).Append(director.playOnAwake ? " onAwake" : "");
                    break;
                case Animator animator:
                    text.Append(" controller ").Append(animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : "-");
                    break;
                case ParticleSystem particles:
                    text.Append(particles.isPlaying ? " playing" : " stopped").Append(" count ").Append(particles.particleCount);
                    break;
                case Renderer renderer:
                    text.Append(" mat ").Append(Mat(renderer.sharedMaterial)).Append(" order ").Append(renderer.sortingOrder).Append(" layer ").Append(renderer.sortingLayerName);
                    break;
            }
            // what the game's own code keeps on it
            if (fields && component is MonoBehaviour && Ours(type)) Fields(component, text);
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
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (field.IsNotSerialized) continue;
                    if (!field.IsPublic && field.GetCustomAttribute<SerializeField>() == null) continue;
                    string value;
                    try { value = Value(field.GetValue(target), 0); }
                    catch (Exception e) { value = "!" + e.GetType().Name; }
                    text.Append(" | ").Append(field.Name).Append('=').Append(value);
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
                case Vector3 v3: return "(" + V(v3) + ")";
                case Color colour: return C(colour);
                case Color32 colour32: return C(colour32);
                case Sprite sprite: return sprite == null ? "null" : "Sprite:" + sprite.name + " " + V(sprite.rect.size);
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
            // a plain serialized class of the game's: its own fields
            var inner = new List<string>();
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (inner.Count >= 12) break;
                try { inner.Add(field.Name + "=" + Value(field.GetValue(value), level + 1)); }
                catch (Exception) { inner.Add(field.Name + "=!"); }
            }
            return type.Name + "{" + string.Join(", ", inner) + "}";
        }
    }
}
