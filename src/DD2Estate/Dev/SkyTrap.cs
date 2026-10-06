using System.Collections;
using System.Collections.Generic;
using System.IO;
using Assets.Code.UI;
using Assets.Code.Utils;
using DD2Estate.Dd2;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DD2Estate.Dev
{
    /// <summary>
    /// Dev: looks at every frame the game puts on screen and, when one is sky-like (a good share of saturated blue),
    /// keeps the frame and writes down, in that same frame, everything that could have drawn it: the cameras, the
    /// canvases, the large graphics, the scenes. A frame taken over the bridge comes a few frames late, when what
    /// drew it may be gone again; this does not. It can also keep every n-th frame small, as a film of a loading.
    ///
    ///   estate.skytrap on=true [folder=...] [film=4]   start (sky-like frames are kept as trap_N.png in the folder,
    ///                                                  every 4th frame as film/NNNNNNN.jpg)
    ///   estate.skytrap                                 frames seen, sky-like ones by the game's mode and the fader's
    ///                                                  state, what was caught, the film's frames
    ///   estate.skytrap on=false                        stop
    /// </summary>
    internal static class SkyTrap
    {
        private const int Keep = 14;
        private const float Share = 0.15f;
        private const int FilmWidth = 480, FilmHeight = 270;

        private static Coroutine _run;
        private static Texture2D _column, _small;
        private static RenderTexture _full;
        private static string _folder;
        private static int _film;
        private static int _frames, _sky, _lastCaught = -1000;
        private static readonly List<object> Caught = new List<object>();
        private static readonly List<object> Film = new List<object>();
        private static readonly Dictionary<string, int> ByMode = new Dictionary<string, int>();

        public static object Set(bool? on, string folder, int? film)
        {
            if (on == true)
            {
                if (_run != null) Plugin.Host.StopCoroutine(_run);
                _folder = folder;
                _film = string.IsNullOrEmpty(folder) ? 0 : film ?? 0;
                if (_film > 0) Directory.CreateDirectory(Path.Combine(_folder, "film"));
                _frames = _sky = 0;
                _lastCaught = -1000;
                Caught.Clear();
                Film.Clear();
                ByMode.Clear();
                _run = Plugin.Host.StartCoroutine(Watch());
            }
            else if (on == false && _run != null)
            {
                Plugin.Host.StopCoroutine(_run);
                _run = null;
            }
            return new { on = _run != null, now = Time.frameCount, frames = _frames, sky = _sky, byMode = ByMode, caught = Caught, film = Film };
        }

        private static IEnumerator Watch()
        {
            var end = new WaitForEndOfFrame();
            while (true)
            {
                yield return end;
                _frames++;
                var where = Assets.Code.Game.GameModeMgr.CurrentMode.GetName() + (EstateSession.Active ? " (estate)" : EstateSession.Starting ? " (estate starting)" : "") + " fader " + Fader();
                var share = Blue();
                if (_film > 0 && _frames % _film == 0) Shoot(where, share);
                if (share < Share) continue;
                _sky++;
                ByMode[where] = (ByMode.TryGetValue(where, out var n) ? n : 0) + 1;
                // the first of a run of such frames, the first of each state within it, and one from well inside
                var fresh = Time.frameCount - _lastCaught >= 20;
                _lastCaught = Time.frameCount;
                if (Caught.Count >= Keep || !(fresh || n == 0 || n == 12)) continue;
                string file = null;
                if (!string.IsNullOrEmpty(_folder))
                {
                    // no texture comes back in some frames (seen while the main menu loads)
                    var shot = ScreenCapture.CaptureScreenshotAsTexture();
                    if (shot != null)
                    {
                        file = Path.Combine(_folder, "trap_" + Caught.Count + ".png");
                        File.WriteAllBytes(file, shot.EncodeToPNG());
                        Object.Destroy(shot);
                    }
                }
                Caught.Add(State(share, file));
            }
        }

        private static string Fader()
        {
            if (!SingletonMonoBehaviour<ScreenFaderBhv>.HasInstance()) return "none";
            var fader = SingletonMonoBehaviour<ScreenFaderBhv>.Instance;
            return fader.IsBlack ? "black" : fader.IsClear ? "clear" : fader.FadingToBlack ? "to black" : "to clear";
        }

        /// <summary>The share of saturated blue down three columns of the screen as it is now.</summary>
        private static float Blue()
        {
            int height = Screen.height, width = Screen.width;
            if (_column == null || _column.height != height) _column = new Texture2D(1, height, TextureFormat.RGB24, false);
            int blue = 0, all = 0;
            for (var i = 1; i <= 3; i++)
            {
                _column.ReadPixels(new Rect(width * i / 4, 0, 1, height), 0, 0, false);
                var pixels = _column.GetPixels32();
                for (var y = 0; y < pixels.Length; y += 4)
                {
                    var p = pixels[y];
                    all++;
                    if (p.b > 90 && p.b > p.r + 45 && p.b > p.g + 20) blue++;
                }
            }
            return all > 0 ? (float)blue / all : 0f;
        }

        /// <summary>The frame as it is on screen, small, into the film (upside down on some graphics APIs: the
        /// reader turns it).</summary>
        private static void Shoot(string where, float share)
        {
            if (_full == null || _full.width != Screen.width || _full.height != Screen.height)
            {
                if (_full != null) _full.Release();
                _full = new RenderTexture(Screen.width, Screen.height, 0);
            }
            if (_small == null) _small = new Texture2D(FilmWidth, FilmHeight, TextureFormat.RGB24, false);
            ScreenCapture.CaptureScreenshotIntoRenderTexture(_full);
            var small = RenderTexture.GetTemporary(FilmWidth, FilmHeight, 0);
            Graphics.Blit(_full, small);
            var before = RenderTexture.active;
            RenderTexture.active = small;
            _small.ReadPixels(new Rect(0, 0, FilmWidth, FilmHeight), 0, 0, false);
            RenderTexture.active = before;
            RenderTexture.ReleaseTemporary(small);
            var file = Path.Combine(_folder, "film", Time.frameCount.ToString("D7") + ".jpg");
            File.WriteAllBytes(file, _small.EncodeToJPG(85));
            Film.Add(new { frame = Time.frameCount, where, share, flipped = SystemInfo.graphicsUVStartsAtTop });
        }

        private static object State(float share, string file)
        {
            var scenes = new List<string>();
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                scenes.Add(scene.name + (scene.isLoaded ? "" : " (loading)"));
            }

            var cameras = new List<object>();
            foreach (var camera in Resources.FindObjectsOfTypeAll<Camera>())
            {
                if (camera == null || !camera.gameObject.scene.IsValid()) continue;
                var data = camera.GetComponent<UniversalAdditionalCameraData>();
                var own = camera.GetComponent<Skybox>();
                cameras.Add(new
                {
                    path = Name(camera.transform), scene = camera.gameObject.scene.name,
                    on = camera.isActiveAndEnabled, clear = camera.clearFlags.ToString(),
                    background = new[] { camera.backgroundColor.r, camera.backgroundColor.g, camera.backgroundColor.b, camera.backgroundColor.a },
                    depth = camera.depth, rect = new[] { camera.rect.x, camera.rect.y, camera.rect.width, camera.rect.height },
                    target = camera.targetTexture != null ? camera.targetTexture.name : null, mask = camera.cullingMask,
                    kind = data != null ? data.renderType.ToString() : null, stack = data != null && data.renderType == CameraRenderType.Base && data.cameraStack != null ? data.cameraStack.Count : 0,
                    post = data != null && data.renderPostProcessing,
                    ownSky = own != null && own.material != null ? own.material.name : null
                });
            }

            var canvases = new List<object>();
            foreach (var canvas in Object.FindObjectsOfType<Canvas>())
            {
                if (canvas == null || !canvas.enabled || !canvas.gameObject.activeInHierarchy) continue;
                if (!canvas.isRootCanvas && !canvas.overrideSorting) continue;
                canvases.Add(new
                {
                    path = Name(canvas.transform), mode = canvas.renderMode.ToString(), order = canvas.sortingOrder, layer = canvas.sortingLayerName,
                    camera = canvas.worldCamera != null ? canvas.worldCamera.name : null
                });
            }

            var graphics = new List<object>();
            foreach (var graphic in Object.FindObjectsOfType<Graphic>())
            {
                if (graphic == null || !graphic.isActiveAndEnabled || graphic.canvas == null || !graphic.canvas.enabled) continue;
                var rect = graphic.rectTransform.rect;
                var scale = graphic.rectTransform.lossyScale;
                if (Mathf.Abs(rect.width * scale.x) < Screen.width * 0.5f || Mathf.Abs(rect.height * scale.y) < Screen.height * 0.1f) continue;
                var alpha = graphic.color.a * graphic.canvasRenderer.GetInheritedAlpha();
                if (alpha < 0.02f) continue;
                var c = graphic.color;
                graphics.Add(new
                {
                    path = Name(graphic.transform), type = graphic.GetType().Name, order = graphic.canvas.sortingOrder,
                    texture = graphic.mainTexture != null ? graphic.mainTexture.name : null,
                    material = graphic.materialForRendering != null ? graphic.materialForRendering.name + " (" + graphic.materialForRendering.shader.name + ")" : null,
                    colour = new[] { c.r, c.g, c.b, alpha }
                });
            }

            var sky = RenderSettings.skybox;
            return new
            {
                frame = Time.frameCount, file, share, fader = Fader(),
                mode = Assets.Code.Game.GameModeMgr.CurrentMode.GetName(), estate = EstateSession.Active, starting = EstateSession.Starting,
                active = SceneManager.GetActiveScene().name, scenes,
                skybox = sky != null ? sky.name + " (" + sky.shader.name + ")" : null,
                fog = Assets.Code.Rendering.DDFog.FogPassEnabled,
                cameras, canvases, graphics
            };
        }

        private static string Name(Transform t)
        {
            var path = t.name;
            for (var p = t.parent; p != null && path.Length < 160; p = p.parent) path = p.name + "/" + path;
            return path;
        }
    }
}
