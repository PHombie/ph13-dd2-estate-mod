using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Assets.Code.Game;
using Assets.Code.UI;
using Assets.Code.Utils;
using DD2Estate.Dd2;
using DD2Estate.Dungeon;
using DD2Estate.Estate;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace DD2Estate.Dev
{
    /// <summary>
    /// Dev: a passage from one of the game's modes into another, frame by frame. The game's own clock is held to a
    /// fixed step while the film runs (Time.captureFramerate), so EVERY frame of a wipe is kept however long the
    /// frame took to write: a thing that is on screen for three frames is three pictures. Beside each picture the
    /// state of whatever could have drawn it is written down: the mode and the fader, the cameras, the post
    /// volumes, the canvases, the pipeline's scale; a change from one frame to the next is listed.
    ///
    ///   fx.film on=true folder=D:/x/film [rate=60] [width=1280] [every=1] [frames=900]
    ///   fx.film                              how far it is (frames kept, the mode, the fader)
    ///   fx.film on=false                     ends it: frames.json in the folder (the frames and the changes)
    ///   fx.fader                             the fader's graphics with their materials' numbers
    ///   fx.pipeline                          the render pipeline: its renderers' features, the cameras, the volumes
    /// </summary>
    [EstateModule]
    internal static class PassageFilm
    {
        private static Coroutine _run;
        private static RenderTexture _full;
        private static Texture2D _small;
        private static string _folder;
        private static int _kept, _limit;
        private static int _rateBefore;
        private static readonly List<Dictionary<string, object>> Frames = new List<Dictionary<string, object>>();
        private static readonly List<object> Changes = new List<object>();
        private static readonly Dictionary<string, string> Last = new Dictionary<string, string>();

        private static void Register()
        {
            AgentBridge.Register("fx.film", o => Film((bool?)o["on"], (string)o["folder"], (int?)o["rate"] ?? 60, (int?)o["width"] ?? 1280, (int?)o["every"] ?? 1, (int?)o["frames"] ?? 900));
            AgentBridge.Register("fx.fader", o => FaderParts());
            AgentBridge.Register("fx.pipeline", o => Pipeline());
            // DD2's four looks of the torch's flame and bar (the materials of its widget, with their numbers)
            AgentBridge.Register("fx.torch", o =>
            {
                if (o["levelTime"] != null) Dd2Torch.LevelTime = Mathf.Max(0f, (float)o["levelTime"]);
                return Dd2Torch.DescribeLevels();
            });
        }

        private static object Film(bool? on, string folder, int rate, int width, int every, int frames)
        {
            if (on == true && !string.IsNullOrEmpty(folder))
            {
                Stop(false);
                Directory.CreateDirectory(folder);
                _folder = folder;
                _kept = 0;
                _limit = Mathf.Clamp(frames, 1, 6000);
                Frames.Clear();
                Changes.Clear();
                Last.Clear();
                _rateBefore = Time.captureFramerate;
                Time.captureFramerate = Mathf.Clamp(rate, 0, 240);
                _run = Plugin.Host.StartCoroutine(Filming(Mathf.Max(1, every), Mathf.Clamp(width, 160, 3840)));
            }
            else if (on == false) Stop(true);
            return new
            {
                on = _run != null, now = Time.frameCount, kept = _kept, folder = _folder, rate = Time.captureFramerate, where = Where(),
                changes = _run == null ? Changes : null
            };
        }

        private static void Stop(bool write)
        {
            if (_run == null) return;
            Plugin.Host.StopCoroutine(_run);
            _run = null;
            Time.captureFramerate = _rateBefore;
            if (!write || string.IsNullOrEmpty(_folder)) return;
            try
            {
                File.WriteAllText(Path.Combine(_folder, "frames.json"), Newtonsoft.Json.JsonConvert.SerializeObject(new { frames = Frames, changes = Changes }, Newtonsoft.Json.Formatting.Indented));
            }
            catch (Exception e) { Plugin.Log.LogWarning("fx.film: the list of frames could not be written: " + e.Message); }
        }

        private static IEnumerator Filming(int every, int width)
        {
            var end = new WaitForEndOfFrame();
            var n = 0;
            while (true)
            {
                yield return end;
                if (n++ % every != 0) continue;
                var height = Mathf.RoundToInt(width * (float)Screen.height / Mathf.Max(1, Screen.width));
                if (_full == null || _full.width != Screen.width || _full.height != Screen.height)
                {
                    if (_full != null) _full.Release();
                    _full = new RenderTexture(Screen.width, Screen.height, 0);
                }
                if (_small == null || _small.width != width || _small.height != height)
                {
                    if (_small != null) UnityEngine.Object.Destroy(_small);
                    _small = new Texture2D(width, height, TextureFormat.RGB24, false);
                }
                ScreenCapture.CaptureScreenshotIntoRenderTexture(_full);
                var small = RenderTexture.GetTemporary(width, height, 0);
                Graphics.Blit(_full, small);
                var before = RenderTexture.active;
                RenderTexture.active = small;
                _small.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                RenderTexture.active = before;
                RenderTexture.ReleaseTemporary(small);
                File.WriteAllBytes(Path.Combine(_folder, Time.frameCount.ToString("D7") + ".jpg"), _small.EncodeToJPG(90));
                _kept++;
                try { Note(); }
                catch (Exception e) { Changes.Add(new { frame = Time.frameCount, what = "note failed", now = e.Message }); }
                if (_kept >= _limit)
                {
                    _run = null;
                    Time.captureFramerate = _rateBefore;
                    try { File.WriteAllText(Path.Combine(_folder, "frames.json"), Newtonsoft.Json.JsonConvert.SerializeObject(new { frames = Frames, changes = Changes }, Newtonsoft.Json.Formatting.Indented)); }
                    catch (Exception) { }
                    yield break;
                }
            }
        }

        // ---- what stands behind a frame ---------------------------------------------------------------------------

        private static string Where()
        {
            var modes = Singleton<GameModeMgr>.Instance;
            var fader = Dd2Fader.Fader;
            var director = Dd2Fader.Director(fader);
            return GameModeMgr.CurrentMode.GetName() + (modes.IsChangingState() ? " -> " + modes.GetNextMode()?.GetName() : "")
                   + " | fader " + (fader == null ? "none" : fader.IsBlack ? "black" : fader.IsClear ? "clear" : fader.FadingToBlack ? "to black" : "to clear")
                   + (director != null && director.playableAsset != null ? " '" + director.playableAsset.name + "' " + director.time.ToString("0.000", CultureInfo.InvariantCulture) : "");
        }

        private static void Note()
        {
            var frame = new Dictionary<string, object>
            {
                ["frame"] = Time.frameCount, ["time"] = Time.unscaledTime, ["game"] = Time.time, ["where"] = Where(),
                ["flipped"] = SystemInfo.graphicsUVStartsAtTop, ["fade"] = CorridorLight.Fade, ["torch"] = CorridorLight.Torch
            };
            Frames.Add(frame);
            Change("cameras", Cameras());
            Change("volumes", Volumes());
            Change("canvases", Canvases());
            Change("screen", Screen.width + "x" + Screen.height + " scale " + N(UniversalRenderPipeline.asset != null ? UniversalRenderPipeline.asset.renderScale : 0f)
                             + " dyn " + N(ScalableBufferManager.widthScaleFactor) + "x" + N(ScalableBufferManager.heightScaleFactor) + " timeScale " + N(Time.timeScale));
            Change("fader", FaderLine());
            Change("corridor", "view " + (CorridorView.Instance != null ? (CorridorView.Instance.isActiveAndEnabled ? "on " : "off ") + CorridorView.Instance.TransitionPhase : "none")
                               + " curtain " + (EstateCurtain.Down ? "down" : "up"));
        }

        private static void Change(string what, string now)
        {
            if (Last.TryGetValue(what, out var before) && before == now) return;
            Last[what] = now;
            if (Changes.Count < 4000) Changes.Add(new { frame = Time.frameCount, what, now });
        }

        private static string Cameras()
        {
            var sb = new StringBuilder();
            var all = Camera.allCameras;
            Array.Sort(all, (a, b) => string.CompareOrdinal(a.name, b.name));
            foreach (var camera in all)
            {
                if (camera == null) continue;
                var t = camera.transform;
                var data = camera.GetUniversalAdditionalCameraData();
                sb.Append(camera.name).Append(" pos ").Append(V(t.position)).Append(" rot ").Append(V(t.eulerAngles)).Append(camera.orthographic ? " ortho " + N(camera.orthographicSize) : " fov " + N(camera.fieldOfView))
                  .Append(" rect ").Append(N(camera.rect.x)).Append(',').Append(N(camera.rect.y)).Append(',').Append(N(camera.rect.width)).Append(',').Append(N(camera.rect.height))
                  .Append(" px ").Append(camera.pixelWidth).Append('x').Append(camera.pixelHeight)
                  .Append(camera.targetTexture != null ? " into " + camera.targetTexture.name + " " + camera.targetTexture.width + "x" + camera.targetTexture.height : "")
                  .Append(data != null ? " " + data.renderType + (data.renderPostProcessing ? " post" : "") + " stack " + (data.cameraStack != null ? data.cameraStack.Count : 0) : "")
                  .Append(" mask ").Append(camera.cullingMask.ToString("X")).Append(" lens ").Append(V(camera.lensShift)).Append("; ");
            }
            return sb.ToString();
        }

        private static string Volumes()
        {
            var sb = new StringBuilder();
            var all = UnityEngine.Object.FindObjectsOfType<Volume>();
            Array.Sort(all, (a, b) => string.CompareOrdinal(a.name, b.name));
            foreach (var volume in all)
            {
                if (volume == null || !volume.isActiveAndEnabled) continue;
                sb.Append(volume.name).Append(" layer ").Append(volume.gameObject.layer).Append(volume.isGlobal ? " global" : " local").Append(" weight ").Append(N(volume.weight)).Append(" prio ").Append(N(volume.priority)).Append(" [");
                var profile = volume.HasInstantiatedProfile() ? volume.profile : volume.sharedProfile;
                if (profile != null)
                    foreach (var component in profile.components)
                    {
                        if (component == null || !component.active) continue;
                        sb.Append(component.GetType().Name).Append('(');
                        foreach (var field in component.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
                        {
                            if (!(field.GetValue(component) is VolumeParameter parameter) || !parameter.overrideState) continue;
                            sb.Append(field.Name).Append('=').Append(parameter).Append(' ');
                        }
                        sb.Append(") ");
                    }
                sb.Append("]; ");
            }
            return sb.ToString();
        }

        private static string Canvases()
        {
            var sb = new StringBuilder();
            var all = UnityEngine.Object.FindObjectsOfType<Canvas>();
            Array.Sort(all, (a, b) => a.sortingOrder != b.sortingOrder ? a.sortingOrder.CompareTo(b.sortingOrder) : string.CompareOrdinal(a.name, b.name));
            foreach (var canvas in all)
            {
                if (canvas == null || !canvas.isRootCanvas || !canvas.isActiveAndEnabled) continue;
                sb.Append(canvas.name).Append(' ').Append(canvas.renderMode == RenderMode.ScreenSpaceOverlay ? "overlay" : canvas.renderMode == RenderMode.ScreenSpaceCamera ? "camera" : "world")
                  .Append(" order ").Append(canvas.sortingOrder).Append(" scale ").Append(N(canvas.scaleFactor)).Append(canvas.worldCamera != null ? " cam " + canvas.worldCamera.name : "").Append("; ");
            }
            return sb.ToString();
        }

        private static string FaderLine()
        {
            var fader = Dd2Fader.Fader;
            if (fader == null) return "none";
            var sb = new StringBuilder();
            sb.Append(fader.m_transitionObject != null && fader.m_transitionObject.activeInHierarchy ? "up" : "away");
            foreach (var graphic in fader.GetComponentsInChildren<Graphic>(false))
            {
                if (graphic == null || !graphic.isActiveAndEnabled) continue;
                var rect = graphic.rectTransform;
                sb.Append(" | ").Append(graphic.name).Append(' ').Append(C(graphic.color)).Append(" at ").Append(N(rect.anchoredPosition.x)).Append(',').Append(N(rect.anchoredPosition.y))
                  .Append(" size ").Append(N(rect.rect.width)).Append('x').Append(N(rect.rect.height)).Append(" scale ").Append(N(rect.lossyScale.x)).Append(',').Append(N(rect.lossyScale.y));
                var group = graphic.GetComponentInParent<CanvasGroup>();
                if (group != null) sb.Append(" group ").Append(N(group.alpha));
            }
            return sb.ToString();
        }

        // ---- the fader's parts, the pipeline ----------------------------------------------------------------------

        private static object FaderParts()
        {
            var fader = Dd2Fader.Fader;
            if (fader == null) return "no fader";
            var parts = new List<object>();
            foreach (var graphic in fader.GetComponentsInChildren<Graphic>(true))
            {
                if (graphic == null) continue;
                parts.Add(new
                {
                    path = AgentBridge.PathOf(graphic.transform), type = graphic.GetType().Name, on = graphic.isActiveAndEnabled, colour = C(graphic.color),
                    texture = graphic.mainTexture != null ? graphic.mainTexture.name + " " + graphic.mainTexture.width + "x" + graphic.mainTexture.height : null,
                    material = MaterialNumbers(graphic.material), drawn = graphic.materialForRendering != graphic.material ? MaterialNumbers(graphic.materialForRendering) : null
                });
            }
            return new { line = FaderLine(), parts };
        }

        /// <summary>A material's shader and every number, colour and texture the shader declares.</summary>
        internal static object MaterialNumbers(Material material)
        {
            if (material == null) return null;
            var shader = material.shader;
            var numbers = new Dictionary<string, object>();
            if (shader != null)
                for (var i = 0; i < shader.GetPropertyCount(); i++)
                {
                    var name = shader.GetPropertyName(i);
                    switch (shader.GetPropertyType(i))
                    {
                        case ShaderPropertyType.Color: numbers[name] = C(material.GetColor(name)); break;
                        case ShaderPropertyType.Vector: numbers[name] = material.GetVector(name).ToString("0.###"); break;
                        case ShaderPropertyType.Float:
                        case ShaderPropertyType.Range: numbers[name] = material.GetFloat(name); break;
                        case ShaderPropertyType.Texture:
                            var texture = material.GetTexture(name);
                            numbers[name] = texture != null ? texture.name + " " + texture.width + "x" + texture.height + " tiling " + material.GetTextureScale(name).ToString("0.###") + " offset " + material.GetTextureOffset(name).ToString("0.###") : "(none)";
                            break;
                    }
                }
            return new { name = material.name, shader = shader != null ? shader.name : null, queue = material.renderQueue, keywords = material.shaderKeywords, passes = material.passCount, numbers };
        }

        private static object Pipeline()
        {
            var asset = UniversalRenderPipeline.asset;
            var renderers = new List<object>();
            if (asset != null)
            {
                var list = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(asset) as ScriptableRendererData[];
                if (list != null)
                    foreach (var data in list)
                    {
                        if (data == null) continue;
                        var features = new List<object>();
                        foreach (var feature in data.rendererFeatures)
                        {
                            if (feature == null) continue;
                            var fields = new Dictionary<string, object>();
                            foreach (var field in feature.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                            {
                                object value;
                                try { value = field.GetValue(feature); }
                                catch (Exception) { continue; }
                                if (value is Material material) fields[field.Name] = material != null ? material.name + " (" + (material.shader != null ? material.shader.name : "") + ")" : null;
                                else if (value is Shader shader) fields[field.Name] = shader != null ? shader.name : null;
                                else if (value is string || value is bool || value is int || value is float || value is Enum) fields[field.Name] = Convert.ToString(value, CultureInfo.InvariantCulture);
                            }
                            features.Add(new { name = feature.name, type = feature.GetType().FullName, active = feature.isActive, fields });
                        }
                        renderers.Add(new { name = data.name, type = data.GetType().Name, features });
                    }
            }
            return new
            {
                asset = asset != null ? asset.name : null, renderScale = asset != null ? asset.renderScale : 0f, opaqueTexture = asset != null && asset.supportsCameraOpaqueTexture,
                depthTexture = asset != null && asset.supportsCameraDepthTexture, hdr = asset != null && asset.supportsHDR, msaa = asset != null ? asset.msaaSampleCount : 0,
                renderers, cameras = Cameras(), volumes = Volumes(), canvases = Canvases()
            };
        }

        private static string N(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        private static string V(Vector2 v) => "(" + N(v.x) + "," + N(v.y) + ")";
        private static string V(Vector3 v) => "(" + N(v.x) + "," + N(v.y) + "," + N(v.z) + ")";
        private static string C(Color c) => "(" + N(c.r) + "," + N(c.g) + "," + N(c.b) + "," + N(c.a) + ")";
    }
}
