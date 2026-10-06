using System.Collections;
using System.Collections.Generic;
using Assets.Code.Game.StageCoach;
using Assets.Code.UI;
using Assets.Code.Utils;
using DD2Estate.Dd2;
using DD2Estate.Estate;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DD2Estate.Dev
{
    /// <summary>
    /// Dev: what of DD2's own world is drawn while an Estate session runs. The Estate shows DD1's places; DD2's
    /// stagecoach, its road and its inn have no business on screen there. A frame photographed over the bridge
    /// comes late and says nothing of what drew it; this looks, frame by frame, at the renderers a camera drew
    /// (Renderer.isVisible) in every scene but the session's own, and writes down each change: the game's mode,
    /// the fader, the scenes with something in view, and whether a stagecoach (or its horses) is among it.
    ///
    ///   estate.scenewatch on=true [every=2]   start (the list of changes is emptied)
    ///   estate.scenewatch                     frames looked at, the changes, the frames with a coach in view by
    ///                                         mode and fader
    ///   estate.scenewatch on=false            stop
    ///   estate.world                          this frame: per scene what is in view, by its top objects; the
    ///                                         coach's objects by path, drawn or not
    ///   estate.party guid=5                   a hero into the party or out of it (in the hamlet)
    ///   estate.film on=true folder=... [every=2] [width=960]   every n-th frame as a JPG, for a GIF; on=false ends
    /// </summary>
    [EstateModule]
    internal static class SceneWatch
    {
        private const int MaxChanges = 600;

        private static Coroutine _run;
        private static int _every = 2, _frames, _looked;
        private static string _last;
        private static readonly List<object> Changes = new List<object>();
        private static readonly Dictionary<string, int> CoachFrames = new Dictionary<string, int>();
        private static readonly Dictionary<int, string> CoachOf = new Dictionary<int, string>();

        private static void Register()
        {
            AgentBridge.Register("estate.scenewatch", o => Set((bool?)o["on"], (int?)o["every"]));
            AgentBridge.Register("estate.world", o => World());
            AgentBridge.Register("estate.film", o => Film((bool?)o["on"], (string)o["folder"], (int?)o["every"] ?? 2, (int?)o["width"] ?? 960));
            // A hero into the party or out of it, as a click on their roster row does in the hamlet ({"guid":5}):
            // a trip that loses a party needs another.
            AgentBridge.Register("estate.party", o =>
            {
                if (o["guid"] != null) EstateSession.ToggleParty((uint)o["guid"]);
                return new { party = EstateSession.PartySize };
            });
        }

        // ---- a film of the screen, for GIFs ---------------------------------------------------------------------

        private static Coroutine _filming;
        private static RenderTexture _full;
        private static Texture2D _small;
        private static readonly List<object> Filmed = new List<object>();

        // estate.film on=true folder=... [every=2] [width=960]: every n-th frame as it is on screen, as
        // <folder>/NNNNNNN.jpg (the skytrap's film is 480 wide and looks for skies besides); on=false ends it and
        // lists the frames with the game's clock at each (upside down on some graphics APIs: "flipped").
        private static object Film(bool? on, string folder, int every, int width)
        {
            if (on == true && !string.IsNullOrEmpty(folder))
            {
                if (_filming != null) Plugin.Host.StopCoroutine(_filming);
                System.IO.Directory.CreateDirectory(folder);
                Filmed.Clear();
                _filming = Plugin.Host.StartCoroutine(Filming(folder, Mathf.Max(1, every), Mathf.Clamp(width, 160, 2560)));
            }
            else if (on == false && _filming != null)
            {
                Plugin.Host.StopCoroutine(_filming);
                _filming = null;
            }
            return new { on = _filming != null, now = Time.frameCount, frames = Filmed };
        }

        private static IEnumerator Filming(string folder, int every, int width)
        {
            var end = new WaitForEndOfFrame();
            var height = Mathf.RoundToInt(width * (float)Screen.height / Mathf.Max(1, Screen.width));
            var n = 0;
            while (true)
            {
                yield return end;
                if (n++ % every != 0) continue;
                if (_full == null || _full.width != Screen.width || _full.height != Screen.height)
                {
                    if (_full != null) _full.Release();
                    _full = new RenderTexture(Screen.width, Screen.height, 0);
                }
                if (_small == null || _small.width != width || _small.height != height)
                {
                    if (_small != null) Object.Destroy(_small);
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
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, Time.frameCount.ToString("D7") + ".jpg"), _small.EncodeToJPG(88));
                Filmed.Add(new { frame = Time.frameCount, time = Time.unscaledTime, flipped = SystemInfo.graphicsUVStartsAtTop });
            }
        }

        private static object Set(bool? on, int? every)
        {
            if (on == true)
            {
                if (_run != null) Plugin.Host.StopCoroutine(_run);
                _every = Mathf.Max(1, every ?? 2);
                _frames = _looked = 0;
                _last = null;
                Changes.Clear();
                CoachFrames.Clear();
                CoachOf.Clear();
                _run = Plugin.Host.StartCoroutine(Watch());
            }
            else if (on == false && _run != null)
            {
                Plugin.Host.StopCoroutine(_run);
                _run = null;
            }
            return new { on = _run != null, now = Time.frameCount, frames = _frames, looked = _looked, coachFrames = CoachFrames, changes = Changes };
        }

        private static IEnumerator Watch()
        {
            var end = new WaitForEndOfFrame();
            while (true)
            {
                yield return end;
                _frames++;
                if (_frames % _every != 0) continue;
                _looked++;
                Look(out var where, out var scenes, out var coach, out var coachPath);
                var signature = where + " | " + scenes + (coach > 0 ? " | COACH" : "");
                if (coach > 0 && !where.Contains("fader black")) CoachFrames[where] = (CoachFrames.TryGetValue(where, out var n) ? n : 0) + 1;
                if (signature == _last) continue;
                _last = signature;
                if (Changes.Count < MaxChanges) Changes.Add(new { frame = Time.frameCount, time = Time.unscaledTime, where, scenes, coach, coachPath });
            }
        }

        private static string Fader()
        {
            if (!SingletonMonoBehaviour<ScreenFaderBhv>.HasInstance()) return "none";
            var fader = SingletonMonoBehaviour<ScreenFaderBhv>.Instance;
            return fader.IsBlack ? "black" : fader.IsClear ? "clear" : fader.FadingToBlack ? "to black" : "to clear";
        }

        private static string Where()
        {
            return Assets.Code.Game.GameModeMgr.CurrentMode.GetName() + (EstateSession.Active ? " (estate)" : EstateSession.Starting ? " (estate starting)" : "") + " fader " + Fader();
        }

        // What a camera drew in this frame outside the session's own scene: per scene the count, and the coach's share.
        private static void Look(out string where, out string scenes, out int coach, out string coachPath)
        {
            where = Where();
            coach = 0;
            coachPath = null;
            var counts = new SortedDictionary<string, int>();
            var own = EstateSession.Scene;
            var planes = Planes();
            foreach (var renderer in Object.FindObjectsOfType<Renderer>())
            {
                if (renderer == null || !renderer.enabled || !renderer.isVisible) continue;
                var scene = renderer.gameObject.scene;
                if (!scene.IsValid() || scene == own) continue;
                if (planes != null && !GeometryUtility.TestPlanesAABB(planes, renderer.bounds)) continue;
                var path = Coach(renderer.transform);
                if (path != null)
                {
                    coach++;
                    if (coachPath == null) coachPath = scene.name + ": " + path;
                }
                // what belongs to no scene of the game's (the mod's own objects kept across scenes) is not its world
                if (scene.name == "DontDestroyOnLoad" && path == null) continue;
                counts[scene.name] = (counts.TryGetValue(scene.name, out var n) ? n : 0) + 1;
            }
            var parts = new List<string>();
            foreach (var pair in counts) parts.Add(pair.Key + " " + pair.Value);
            scenes = parts.Count > 0 ? string.Join(", ", parts) : "nothing of DD2's world";
        }

        // Renderer.isVisible is true for whatever any camera culled in (a light's shadow pass too): what counts as
        // drawn here must also lie in the main camera's view.
        private static Plane[] Planes()
        {
            var camera = Camera.main;
            return camera != null ? GeometryUtility.CalculateFrustumPlanes(camera) : null;
        }

        /// <summary>The path of the stagecoach (or horse) object a transform belongs to; null when it belongs to none.</summary>
        internal static string Coach(Transform t)
        {
            if (t == null) return null;
            var id = t.GetInstanceID();
            if (CoachOf.TryGetValue(id, out var known)) return known;
            string found = null;
            for (var p = t; p != null; p = p.parent)
            {
                var name = p.name.ToLowerInvariant();
                if (name.Contains("stagecoach") || name.Contains("stage_coach") || name.Contains("wagon") || name.Contains("horse") || name.Contains("coach")
                    || p.GetComponent<StageCoachSkinSpawnerBhv>() != null || p.GetComponent<WagonBhv>() != null || p.GetComponent<HorseBhv>() != null)
                    found = Path(p);
            }
            CoachOf[id] = found;
            return found;
        }

        private static object World()
        {
            var own = EstateSession.Scene;
            var byScene = new SortedDictionary<string, SortedDictionary<string, int>>();
            var coach = new SortedDictionary<string, int[]>();
            var planes = Planes();
            foreach (var renderer in Resources.FindObjectsOfTypeAll<Renderer>())
            {
                if (renderer == null) continue;
                var scene = renderer.gameObject.scene;
                if (!scene.IsValid() || scene == own) continue;
                var drawn = renderer.enabled && renderer.gameObject.activeInHierarchy && renderer.isVisible
                            && (planes == null || GeometryUtility.TestPlanesAABB(planes, renderer.bounds));
                var path = Coach(renderer.transform);
                if (path != null)
                {
                    var key = scene.name + ": " + path;
                    if (!coach.TryGetValue(key, out var pair)) coach[key] = pair = new int[2];
                    pair[0]++;
                    if (drawn) pair[1]++;
                }
                if (!drawn) continue;
                if (!byScene.TryGetValue(scene.name, out var tops)) byScene[scene.name] = tops = new SortedDictionary<string, int>();
                var top = Top(renderer.transform);
                tops[top] = (tops.TryGetValue(top, out var n) ? n : 0) + 1;
            }
            var coaches = new List<object>();
            foreach (var pair in coach) coaches.Add(new { path = pair.Key, renderers = pair.Value[0], drawn = pair.Value[1] });
            var loaded = new List<string>();
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                loaded.Add(scene.name + (scene.isLoaded ? "" : " (loading)") + (scene == SceneManager.GetActiveScene() ? " (active)" : ""));
            }
            return new { where = Where(), loaded, drawn = byScene, coaches };
        }

        // The object two steps under the scene's root that a renderer belongs to: enough to name a set's parts.
        private static string Top(Transform t)
        {
            var chain = new List<string>();
            for (var p = t; p != null; p = p.parent) chain.Add(p.name);
            chain.Reverse();
            return string.Join("/", chain.GetRange(0, Mathf.Min(3, chain.Count)));
        }

        private static string Path(Transform t)
        {
            var path = t.name;
            for (var p = t.parent; p != null && path.Length < 200; p = p.parent) path = p.name + "/" + path;
            return path;
        }
    }
}
