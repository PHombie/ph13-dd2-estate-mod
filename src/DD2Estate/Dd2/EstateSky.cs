using System.Collections.Generic;
using Assets.Code.UI.Screens;
using Assets.Code.Utils;
using DD2Estate.Dev;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace DD2Estate.Dd2
{
    /// <summary>
    /// The blue-above-white screen that showed while the Estate loaded, on the way into a fight and back, on the way
    /// to the main menu, and (blurred) under the pause menu. What it was, found with the dev frame trap (SkyTrap):
    /// the game's fog. DDFog is a pass of the game's renderer that lays fog over what the main camera drew, by depth,
    /// with the settings the last scene left. The Estate's hub has nothing of the game's world in view: the hamlet is
    /// UI, the dungeon is the mod's own flat scene (drawn after the fog, so it covers it). Whenever neither was up,
    /// the camera had drawn nothing, and fog over nothing is the fog's own picture: its sky tint above the horizon,
    /// its ground haze below.
    /// <list type="bullet">
    /// <item>The fog pass is off while the Estate is in its hub mode (<see cref="Fog"/>) and as the game has it in
    /// a fight, whose arena needs it. Nothing of the hub's own picture changes with it (compared frame for frame).</item>
    /// <item>The game's blur layer (ScreenStackBhv's BlurCanvas, under the pause menu and under any screen of a
    /// layer above it) blurs the camera's picture, which has none of the hub's UI in it. In the hub it is a plain
    /// dark veil (<see cref="Veil"/>): the screen stays where it was, dimmed.</item>
    /// <item>A camera that clears to "the skybox" in a scene without a sky material clears to its background
    /// colour, whatever that was left at; in the Estate that is black (<see cref="Keep"/>). A fight's arena has a
    /// sky of its own and keeps it.</item>
    /// </list>
    /// </summary>
    internal static class EstateSky
    {
        private struct Was
        {
            public CameraClearFlags Flags;
            public Color Colour;
        }

        private static readonly Dictionary<Camera, Was> Held = new Dictionary<Camera, Was>();
        private static readonly List<Camera> Gone = new List<Camera>();
        private static float _nextSweep;

        /// <summary>How dark the veil is.</summary>
        public static float VeilAlpha = 0.72f;

        private static Image _blur;
        private static Material _blurMaterial;
        private static Color _blurColour;
        private static bool _veiled;

        private static bool _estate, _hooked, _fogOff;

        /// <summary>The game's fog pass as the game has it (on), or not run at all (off).</summary>
        public static void Fog(bool on)
        {
            if (on == !_fogOff) return;
            _fogOff = !on;
            Assets.Code.Rendering.DDFog.FogPassEnabled = on;
        }

        public static void Keep(bool estate)
        {
            _estate = estate;
            if (!_hooked)
            {
                // each camera is looked at once more as it is about to draw: one switched on after this frame's update
                // would draw a frame with what it came with
                _hooked = true;
                UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += (context, camera) =>
                {
                    if (!_estate || camera == null || camera.clearFlags != CameraClearFlags.Skybox || RenderSettings.skybox != null) return;
                    Blacken(camera);
                };
            }
            // a scene with a sky of its own (a fight's arena) has its cameras as the game made them
            if (!estate || RenderSettings.skybox != null)
            {
                Restore();
                return;
            }
            foreach (var camera in Camera.allCameras) Blacken(camera);
            // cameras that are switched off and rendered by hand are in no list of the engine's: looked for now and then
            if (Time.unscaledTime >= _nextSweep)
            {
                _nextSweep = Time.unscaledTime + 0.5f;
                foreach (var camera in Resources.FindObjectsOfTypeAll<Camera>()) Blacken(camera);
                Gone.Clear();
                foreach (var pair in Held)
                    if (pair.Key == null) Gone.Add(pair.Key);
                foreach (var camera in Gone) Held.Remove(camera);
            }
        }

        private static void Blacken(Camera camera)
        {
            if (camera == null || camera.clearFlags != CameraClearFlags.Skybox) return;
            // a prefab's own camera (never in a scene) is left alone
            if (!camera.gameObject.scene.IsValid()) return;
            Held[camera] = new Was { Flags = camera.clearFlags, Colour = camera.backgroundColor };
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
        }

        private static void Restore()
        {
            if (Held.Count == 0) return;
            foreach (var pair in Held)
            {
                if (pair.Key == null) continue;
                pair.Key.clearFlags = pair.Value.Flags;
                pair.Key.backgroundColor = pair.Value.Colour;
            }
            Held.Clear();
        }

        /// <summary>The game's blur layer as a plain dark veil (on), or as the game has it (off).</summary>
        public static void Veil(bool on)
        {
            if (on == _veiled && (!on || _blur != null)) return;
            if (_blur == null)
            {
                if (!on || !SingletonMonoBehaviour<ScreenStackBhv>.HasInstance()) return;
                var canvas = AccessTools.Field(typeof(ScreenStackBhv), "m_BlurCanvas")?.GetValue(SingletonMonoBehaviour<ScreenStackBhv>.Instance) as Canvas;
                _blur = canvas != null ? canvas.GetComponent<Image>() : null;
                if (_blur == null) return;
                _blurMaterial = _blur.material;
                _blurColour = _blur.color;
            }
            _veiled = on;
            if (on)
            {
                _blur.material = null;
                _blur.color = new Color(0f, 0f, 0f, VeilAlpha);
            }
            else
            {
                _blur.material = _blurMaterial;
                _blur.color = _blurColour;
            }
        }

        /// <summary>Dev bridge: what could fill the screen right now: the large UI graphics and the large renderers that are on.</summary>
        internal static object Screen()
        {
            var graphics = new List<object>();
            foreach (var graphic in Object.FindObjectsOfType<Graphic>())
            {
                if (graphic == null || !graphic.isActiveAndEnabled || graphic.canvas == null || !graphic.canvas.enabled) continue;
                var rect = graphic.rectTransform.rect;
                var scale = graphic.rectTransform.lossyScale;
                if (Mathf.Abs(rect.width * scale.x) < UnityEngine.Screen.width * 0.6f || Mathf.Abs(rect.height * scale.y) < UnityEngine.Screen.height * 0.3f) continue;
                var c = graphic.color;
                if (c.a * graphic.canvasRenderer.GetInheritedAlpha() < 0.02f) continue;
                graphics.Add(new
                {
                    path = Path(graphic.transform), type = graphic.GetType().Name, canvas = graphic.canvas.rootCanvas.name, order = graphic.canvas.sortingOrder,
                    texture = graphic.mainTexture != null ? graphic.mainTexture.name : null,
                    material = graphic.material != null ? graphic.material.name + " (" + graphic.material.shader.name + ")" : null,
                    colour = new[] { c.r, c.g, c.b, c.a * graphic.canvasRenderer.GetInheritedAlpha() }
                });
            }
            var renderers = new List<object>();
            foreach (var renderer in Object.FindObjectsOfType<Renderer>())
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                var size = renderer.bounds.size;
                if (size.x < 20f && size.y < 20f) continue;
                var material = renderer.sharedMaterial;
                renderers.Add(new
                {
                    path = Path(renderer.transform), type = renderer.GetType().Name, layer = LayerMask.LayerToName(renderer.gameObject.layer), scene = renderer.gameObject.scene.name,
                    size = new[] { size.x, size.y, size.z }, material = material != null ? material.name + " (" + material.shader.name + ")" : null
                });
            }
            return new { mode = Assets.Code.Game.GameModeMgr.CurrentMode.GetName(), scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name, graphics, renderers };
        }

        private static string Path(Transform t)
        {
            var path = t.name;
            for (var p = t.parent; p != null && path.Length < 160; p = p.parent) path = p.name + "/" + path;
            return path;
        }

        /// <summary>Dev bridge: every camera there is, the disabled too, and how each clears.</summary>
        internal static object Describe()
        {
            var list = new List<object>();
            foreach (var camera in Resources.FindObjectsOfTypeAll<Camera>())
            {
                if (camera == null) continue;
                list.Add(new
                {
                    name = camera.name,
                    scene = camera.gameObject.scene.IsValid() ? camera.gameObject.scene.name : "(none)",
                    enabled = camera.enabled,
                    active = camera.gameObject.activeInHierarchy,
                    clear = camera.clearFlags.ToString(),
                    background = new[] { camera.backgroundColor.r, camera.backgroundColor.g, camera.backgroundColor.b },
                    target = camera.targetTexture != null ? camera.targetTexture.name : null,
                    held = Held.ContainsKey(camera)
                });
            }
            return new { skybox = RenderSettings.skybox != null ? RenderSettings.skybox.name : null, veiled = _veiled, fog = Assets.Code.Rendering.DDFog.FogPassEnabled, cameras = list };
        }
    }
}
