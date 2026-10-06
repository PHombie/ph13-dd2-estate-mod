using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using Assets.Code.UI;
using Assets.Code.UI.Transitions;
using Assets.Code.Utils;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.UI;

namespace DD2Estate.Dd2
{
    /// <summary>
    /// DD2's own loading screen, as the mod reaches it: the game has no loading scene with a picture (its
    /// "Loading" scene is not in the build; "EmptyLoading" is empty). What the player sees while DD2 loads is its
    /// screen fader (<see cref="ScreenFaderBhv"/>, a canvas over everything): a timeline closes black over the
    /// screen (a plain fade, or a wipe from one of the four sides), a small sign (the "throbber", a
    /// PlayableDirector with an open, an idle and a close timeline) turns in a corner while the game loads, and
    /// another timeline lifts the black again.
    /// This class reads the fader's private parts and describes them for the dev bridge.
    /// </summary>
    internal static class Dd2Fader
    {
        private static readonly FieldInfo ThrobberField = AccessTools.Field(typeof(ScreenFaderBhv), "m_throbber");
        private static readonly FieldInfo DirectorField = AccessTools.Field(typeof(ScreenFaderBhv), "m_playableDirector");
        private static readonly FieldInfo OpenField = AccessTools.Field(typeof(ScreenFaderBhv), "m_throbberOpen");
        private static readonly FieldInfo IdleField = AccessTools.Field(typeof(ScreenFaderBhv), "m_throbberIdle");
        private static readonly FieldInfo CloseField = AccessTools.Field(typeof(ScreenFaderBhv), "m_throbberClose");
        private static readonly FieldInfo TransitionsField = AccessTools.Field(typeof(ScreenFaderBhv), "m_transitions");

        public static ScreenFaderBhv Fader => SingletonMonoBehaviour<ScreenFaderBhv>.HasInstance() ? SingletonMonoBehaviour<ScreenFaderBhv>.Instance : null;

        public static PlayableDirector Throbber(ScreenFaderBhv fader) => fader != null ? ThrobberField?.GetValue(fader) as PlayableDirector : null;
        public static PlayableDirector Director(ScreenFaderBhv fader) => fader != null ? DirectorField?.GetValue(fader) as PlayableDirector : null;
        public static PlayableAsset ThrobberOpen(ScreenFaderBhv fader) => fader != null ? OpenField?.GetValue(fader) as PlayableAsset : null;
        public static PlayableAsset ThrobberIdle(ScreenFaderBhv fader) => fader != null ? IdleField?.GetValue(fader) as PlayableAsset : null;
        public static PlayableAsset ThrobberClose(ScreenFaderBhv fader) => fader != null ? CloseField?.GetValue(fader) as PlayableAsset : null;

        /// <summary>The timeline that closes black (<paramref name="toBlack"/>) or lifts it, of one kind of transition.</summary>
        public static PlayableAsset Transition(ScreenFaderBhv fader, TransitionType type, bool toBlack)
        {
            var list = fader != null ? TransitionsField?.GetValue(fader) as List<SceneTransitionDefinition> : null;
            if (list == null || type == null || type.EnumValue < 0 || type.EnumValue >= list.Count || list[type.EnumValue] == null) return null;
            return toBlack ? list[type.EnumValue].m_toBlack : list[type.EnumValue].m_fromBlack;
        }

        // ---- driving it -----------------------------------------------------------------------------------------

        /// <summary>
        /// Black closes over the screen by one of the game's transitions, its sign in the corner. With
        /// <paramref name="atOnce"/>, or when no such timeline is to be had, black is there from one frame to the
        /// next. A fader that is black already, or closing, is left to it (its sign is put up).
        /// </summary>
        public static void Close(TransitionType type, bool atOnce = false)
        {
            var fader = Fader;
            if (fader == null) return;
            if (fader.IsBlack || fader.FadingToBlack)
            {
                fader.SetThrobberVisible(true);
                return;
            }
            if (atOnce || Transition(fader, type, true) == null) fader.SetToBlack(true);
            else fader.PlayTransition(type, fadingToBlack: true, showThrobber: true);
        }

        /// <summary>Black lifts by one of the game's transitions and its sign closes; nothing is done to a fader that is clear or lifting.</summary>
        public static void Lift(TransitionType type, bool atOnce = false)
        {
            var fader = Fader;
            if (fader == null || fader.IsClear || fader.FadingOutOfBlack) return;
            if (atOnce || Transition(fader, type, false) == null) fader.SetToClear();
            else fader.PlayTransition(type, fadingToBlack: false, showThrobber: false);
        }

        /// <summary>
        /// The object DD2 shows a line of the narrator's words with (SubtitlesMgr: a band, the words on it), which
        /// it also does over its loading black; not on screen while nobody speaks. Null when it cannot be had.
        /// </summary>
        public static RectTransform SubtitleTemplate()
        {
            try
            {
                if (!SingletonMonoBehaviour<Assets.Code.Subtitles.SubtitlesMgr>.HasInstance()) return null;
                return AccessTools.Field(typeof(Assets.Code.Subtitles.SubtitlesMgr), "m_subtitlesTransform")?.GetValue(SingletonMonoBehaviour<Assets.Code.Subtitles.SubtitlesMgr>.Instance) as RectTransform;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Loading screen: DD2's subtitle line could not be reached: " + e.Message);
                return null;
            }
        }

        private static readonly Dictionary<string, Material> Presets = new Dictionary<string, Material>();
        private static readonly HashSet<string> PresetsAsked = new HashSet<string>();

        /// <summary>
        /// One of the looks DD2 gives its title font ("NDDunkelD-Bold SDF TitleCard_Large": the material presets
        /// beside the font asset). They are loaded with the font's bundle only when a screen needs them: the first
        /// call asks the game's asset system for it and returns null, a later one has it.
        /// </summary>
        public static Material FontPreset(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (Presets.TryGetValue(name, out var known) && known != null) return known;
            foreach (var material in Resources.FindObjectsOfTypeAll<Material>())
                if (material != null && material.name == name) return Presets[name] = material;
            if (!PresetsAsked.Add(name)) return null;
            try
            {
                var file = name + ".mat";
                foreach (var locator in UnityEngine.AddressableAssets.Addressables.ResourceLocators)
                    foreach (var key in locator.Keys)
                    {
                        if (!(key is string address) || !address.EndsWith(file, StringComparison.OrdinalIgnoreCase)) continue;
                        var handle = UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<Material>(address);
                        handle.Completed += done =>
                        {
                            if (done.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded && done.Result != null) Presets[name] = done.Result;
                            else Plugin.Log.LogWarning("Loading screen: DD2's font look '" + name + "' could not be loaded");
                        };
                        return null;
                    }
                Plugin.Log.LogWarning("Loading screen: the game has no font look named '" + name + "'");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Loading screen: DD2's font look '" + name + "' could not be asked for: " + e.Message); }
            return null;
        }

        /// <summary>What a text material does to its letters (the numbers of TextMeshPro's shader), for the dev bridge.</summary>
        public static object MaterialInfo(Material material)
        {
            if (material == null) return null;
            var numbers = new Dictionary<string, object>();
            foreach (var colour in new[] { "_FaceColor", "_OutlineColor", "_UnderlayColor", "_GlowColor" })
                if (material.HasProperty(colour)) numbers[colour] = C(material.GetColor(colour));
            foreach (var number in new[] { "_FaceDilate", "_OutlineWidth", "_OutlineSoftness", "_UnderlayOffsetX", "_UnderlayOffsetY", "_UnderlayDilate", "_UnderlaySoftness", "_GlowOffset", "_GlowInner", "_GlowOuter", "_GlowPower", "_GradientScale", "_ScaleRatioA", "_ScaleRatioC" })
                if (material.HasProperty(number)) numbers[number] = material.GetFloat(number);
            return new { name = material.name, shader = material.shader != null ? material.shader.name : null, keywords = material.shaderKeywords, numbers };
        }

        // ---- dev bridge ----------------------------------------------------------------------------------------

        /// <summary>
        /// The fader as it stands: its state, its timelines with their lengths and tracks, and its objects with
        /// what each draws. <paramref name="path"/>: another object to list in place of the fader's.
        /// </summary>
        public static object Describe(string path, int depth, bool fonts, bool texts = false)
        {
            if (texts)
            {
                // every label on screen now: how DD2 sets its words (font, size, style, colour), by what is written
                var lines = new List<string>();
                foreach (var text in UnityEngine.Object.FindObjectsOfType<TMP_Text>())
                {
                    if (text == null || !text.isActiveAndEnabled || string.IsNullOrWhiteSpace(text.text)) continue;
                    if (!string.IsNullOrEmpty(path) && PathOf(text.transform).IndexOf(path, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var sb = new StringBuilder();
                    Dump(text.transform, 0, 0, sb);
                    lines.Add(PathOf(text.transform) + "\n  " + sb.ToString().Trim());
                }
                lines.Sort(StringComparer.Ordinal);
                return lines;
            }
            var fader = Fader;
            var result = new Dictionary<string, object>();
            if (fader == null) result["fader"] = "none yet";
            else
            {
                var canvas = fader.GetComponent<Canvas>();
                var throbber = Throbber(fader);
                var director = Director(fader);
                result["fader"] = new
                {
                    path = PathOf(fader.transform),
                    state = fader.IsBlack ? "black" : fader.IsClear ? "clear" : fader.FadingToBlack ? "to black" : "to clear",
                    blocking = fader.IsBlocking,
                    canvasOrder = canvas != null ? canvas.sortingOrder : 0,
                    canvasMode = canvas != null ? canvas.renderMode.ToString() : null,
                    transitionObject = fader.m_transitionObject != null ? PathOf(fader.m_transitionObject.transform) : null,
                    director = director != null ? PathOf(director.transform) : null,
                    playing = director != null && director.playableAsset != null ? director.playableAsset.name : null,
                    throbber = throbber != null ? PathOf(throbber.transform) : null,
                    throbberUp = throbber != null && throbber.gameObject.activeInHierarchy,
                    throbberPlaying = throbber != null && throbber.playableAsset != null ? throbber.playableAsset.name : null
                };
                var timelines = new List<object>();
                foreach (var type in new[] { TransitionType.FADE, TransitionType.LEFT_TO_RIGHT, TransitionType.RIGHT_TO_LEFT, TransitionType.TOP_TO_BOTTOM, TransitionType.BOTTOM_TO_TOP })
                {
                    timelines.Add(Timeline(type + " to black", Transition(fader, type, true), director));
                    timelines.Add(Timeline(type + " from black", Transition(fader, type, false), director));
                }
                timelines.Add(Timeline("throbber open", ThrobberOpen(fader), throbber));
                timelines.Add(Timeline("throbber idle", ThrobberIdle(fader), throbber));
                timelines.Add(Timeline("throbber close", ThrobberClose(fader), throbber));
                result["timelines"] = timelines;
            }

            Transform root = null;
            if (!string.IsNullOrEmpty(path))
            {
                var go = Dev.AgentBridge.FindObject(path);
                if (go == null) result["objects"] = "not found: " + path;
                else root = go.transform;
            }
            else if (fader != null) root = fader.transform;
            if (root != null)
            {
                var sb = new StringBuilder();
                Dump(root, 0, depth, sb);
                result["objects"] = sb.ToString();
            }

            if (fonts)
            {
                var list = new List<string>();
                foreach (var font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                {
                    if (font == null) continue;
                    var face = font.faceInfo;
                    list.Add(font.name + "  (" + face.familyName + " " + face.styleName + ", point " + face.pointSize.ToString(CultureInfo.InvariantCulture) + ", line " + face.lineHeight.ToString("0.#", CultureInfo.InvariantCulture)
                             + ", cap " + face.capLine.ToString("0.#", CultureInfo.InvariantCulture) + ", material " + (font.material != null ? font.material.name : "none") + ")");
                }
                list.Sort(StringComparer.Ordinal);
                result["fonts"] = list;
            }
            return result;
        }

        // A timeline's length and what it moves: its tracks, what each is bound to, its clips. Unity.Timeline is not
        // among the mod's references: its types are read by name.
        private static object Timeline(string role, PlayableAsset asset, PlayableDirector director)
        {
            if (asset == null) return new { role, asset = (string)null };
            var tracks = new List<string>();
            try
            {
                var get = asset.GetType().GetMethod("GetOutputTracks", BindingFlags.Instance | BindingFlags.Public);
                if (get?.Invoke(asset, null) is IEnumerable all)
                    foreach (var track in all)
                    {
                        if (!(track is UnityEngine.Object named)) continue;
                        var line = new StringBuilder(track.GetType().Name).Append(" '").Append(named.name).Append('\'');
                        var bound = director != null ? director.GetGenericBinding(named) : null;
                        if (bound != null) line.Append(" -> ").Append(bound is Component c ? PathOf(c.transform) + ":" + c.GetType().Name : bound is GameObject g ? PathOf(g.transform) : bound.name);
                        var clips = track.GetType().GetMethod("GetClips", BindingFlags.Instance | BindingFlags.Public)?.Invoke(track, null) as IEnumerable;
                        if (clips != null)
                            foreach (var clip in clips)
                            {
                                var t = clip.GetType();
                                line.Append(" | ").Append(t.GetProperty("displayName")?.GetValue(clip, null)).Append(' ')
                                    .Append(Convert.ToDouble(t.GetProperty("start")?.GetValue(clip, null) ?? 0.0).ToString("0.###", CultureInfo.InvariantCulture)).Append("..")
                                    .Append(Convert.ToDouble(t.GetProperty("end")?.GetValue(clip, null) ?? 0.0).ToString("0.###", CultureInfo.InvariantCulture));
                            }
                        var curves = track.GetType().GetProperty("infiniteClip", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(track, null) as AnimationClip;
                        if (curves != null) line.Append(" | curves '").Append(curves.name).Append("' ").Append(curves.length.ToString("0.###", CultureInfo.InvariantCulture)).Append(" s");
                        tracks.Add(line.ToString());
                    }
            }
            catch (Exception e) { tracks.Add("tracks could not be read: " + e.Message); }
            return new { role, asset = asset.name, seconds = asset.duration, tracks };
        }

        /// <summary>An object and what is under it, a line each: where it stands on screen (pixels, y down) and what it draws.</summary>
        public static void Dump(Transform t, int level, int depth, StringBuilder sb)
        {
            sb.Append(' ', level * 2).Append(t.gameObject.activeInHierarchy ? "" : t.gameObject.activeSelf ? "(off above) " : "(off) ").Append(t.name);
            if (t is RectTransform rt)
            {
                var corners = new Vector3[4];
                rt.GetWorldCorners(corners);
                var canvas = t.GetComponentInParent<Canvas>(true);
                var root = canvas != null ? canvas.rootCanvas : null;
                var camera = root != null && root.renderMode != RenderMode.ScreenSpaceOverlay ? root.worldCamera : null;
                Vector2 a = RectTransformUtility.WorldToScreenPoint(camera, corners[0]), b = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
                sb.Append("  @").Append(N(Mathf.Min(a.x, b.x))).Append(',').Append(N(Screen.height - Mathf.Max(a.y, b.y))).Append(' ').Append(N(Mathf.Abs(b.x - a.x))).Append('x').Append(N(Mathf.Abs(b.y - a.y)));
                sb.Append(" rect ").Append(N(rt.rect.width)).Append('x').Append(N(rt.rect.height)).Append(" anchors ").Append(V(rt.anchorMin)).Append('-').Append(V(rt.anchorMax))
                  .Append(" pivot ").Append(V(rt.pivot)).Append(" pos ").Append(V(rt.anchoredPosition));
            }
            if (t.localScale != Vector3.one) sb.Append(" scale ").Append(V(t.localScale));
            if (t.localEulerAngles != Vector3.zero) sb.Append(" turned ").Append(N(t.localEulerAngles.z));
            foreach (var c in t.GetComponents<Component>())
            {
                if (c == null || c is Transform || c is CanvasRenderer) continue;
                sb.Append("\n").Append(' ', level * 2 + 4).Append("- ").Append(c.GetType().Name);
                if (c is Behaviour behaviour && !behaviour.enabled) sb.Append(" (disabled)");
                try { Part(c, sb); }
                catch (Exception e) { sb.Append(" !").Append(e.GetType().Name); }
            }
            sb.Append('\n');
            if (level >= depth)
            {
                if (t.childCount > 0) sb.Append(' ', (level + 1) * 2).Append("... ").Append(t.childCount).Append(" children\n");
                return;
            }
            for (var i = 0; i < t.childCount; i++) Dump(t.GetChild(i), level + 1, depth, sb);
        }

        private static void Part(Component c, StringBuilder sb)
        {
            switch (c)
            {
                case Canvas canvas:
                    sb.Append(' ').Append(canvas.renderMode).Append(" order ").Append(canvas.sortingOrder).Append(canvas.overrideSorting ? " (own)" : "").Append(" layer '").Append(canvas.sortingLayerName).Append("' scale ")
                      .Append(N(canvas.scaleFactor)).Append(canvas.worldCamera != null ? " camera " + canvas.worldCamera.name : "");
                    break;
                case CanvasScaler scaler:
                    sb.Append(' ').Append(scaler.uiScaleMode).Append(" ref ").Append(V(scaler.referenceResolution)).Append(' ').Append(scaler.screenMatchMode).Append(" match ").Append(N(scaler.matchWidthOrHeight));
                    break;
                case CanvasGroup group:
                    sb.Append(" alpha ").Append(N(group.alpha)).Append(group.blocksRaycasts ? " blocks" : "");
                    break;
                case Image image:
                    sb.Append(" sprite ").Append(image.sprite != null ? "'" + image.sprite.name + "' " + N(image.sprite.rect.width) + "x" + N(image.sprite.rect.height) + " of '" + (image.sprite.texture != null ? image.sprite.texture.name : "") + "'" : "none")
                      .Append(' ').Append(image.type).Append(image.preserveAspect ? " aspect" : "").Append(" colour ").Append(C(image.color)).Append(Mat(image.material, image.defaultMaterial)).Append(image.raycastTarget ? " raycast" : "");
                    break;
                case RawImage raw:
                    sb.Append(" texture ").Append(raw.texture != null ? "'" + raw.texture.name + "' " + raw.texture.width + "x" + raw.texture.height : "none").Append(" colour ").Append(C(raw.color)).Append(Mat(raw.material, raw.defaultMaterial));
                    break;
                case TMP_Text text:
                    sb.Append(" \"").Append((text.text ?? "").Replace("\n", "\\n")).Append("\" font '").Append(text.font != null ? text.font.name : "none").Append("' size ").Append(N(text.fontSize))
                      .Append(text.enableAutoSizing ? " (auto " + N(text.fontSizeMin) + ".." + N(text.fontSizeMax) + ")" : "").Append(' ').Append(text.fontStyle).Append(' ').Append(text.alignment)
                      .Append(" colour ").Append(C(text.color)).Append(" spacing ").Append(N(text.characterSpacing)).Append('/').Append(N(text.wordSpacing)).Append('/').Append(N(text.lineSpacing))
                      .Append(" material '").Append(text.fontSharedMaterial != null ? text.fontSharedMaterial.name : "none").Append('\'');
                    break;
                case PlayableDirector director:
                    sb.Append(" asset ").Append(director.playableAsset != null ? "'" + director.playableAsset.name + "' " + director.playableAsset.duration.ToString("0.###", CultureInfo.InvariantCulture) + " s" : "none")
                      .Append(" at ").Append(director.time.ToString("0.###", CultureInfo.InvariantCulture)).Append(' ').Append(director.state).Append(" wrap ").Append(director.extrapolationMode).Append(" clock ").Append(director.timeUpdateMode);
                    break;
                case Animator animator:
                    sb.Append(" controller ").Append(animator.runtimeAnimatorController != null ? "'" + animator.runtimeAnimatorController.name + "'" : "none").Append(" update ").Append(animator.updateMode);
                    break;
                case ParticleSystem particles:
                    sb.Append(" particles ").Append(particles.particleCount).Append(particles.isPlaying ? " playing" : "");
                    break;
                case Renderer renderer:
                    sb.Append(" material '").Append(renderer.sharedMaterial != null ? renderer.sharedMaterial.name + "' shader '" + renderer.sharedMaterial.shader.name : "none").Append("' order ").Append(renderer.sortingOrder);
                    break;
                case Graphic graphic:
                    sb.Append(" colour ").Append(C(graphic.color)).Append(Mat(graphic.material, graphic.defaultMaterial));
                    break;
            }
        }

        private static string Mat(Material material, Material stock)
        {
            if (material == null || material == stock) return "";
            return " material '" + material.name + "' shader '" + (material.shader != null ? material.shader.name : "") + "'";
        }

        private static string N(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        private static string V(Vector2 v) => "(" + N(v.x) + "," + N(v.y) + ")";
        private static string V(Vector3 v) => "(" + N(v.x) + "," + N(v.y) + "," + N(v.z) + ")";
        private static string C(Color c) => "(" + N(c.r) + "," + N(c.g) + "," + N(c.b) + "," + N(c.a) + ")";

        public static string PathOf(Transform t)
        {
            var path = t.name;
            for (var p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
            return path;
        }
    }
}
