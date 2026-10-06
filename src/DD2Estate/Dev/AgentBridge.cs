using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DD2Estate.Dev
{
    /// <summary>
    /// Dev-only JSON-lines bridge on 127.0.0.1 so the game can be observed and driven without pixels.
    /// Off unless [Dev] BridgePort is set in the plugin config. One request per line, one reply per line;
    /// every request is handled on the main thread so a reply describes one consistent frame.
    ///
    ///   {"cmd":"observe","filter":"..."}   clickable UI as text (id, label, path, screen pos)
    ///   {"cmd":"click","id":3} | {"cmd":"click","path":"Canvas/.../Button"}
    ///   {"cmd":"tree","path":"Root/Child","depth":2}
    ///   {"cmd":"get","expr":"Namespace.Type.StaticMember.Field"}   reflection read
    ///   {"cmd":"members","expr":"..."}     list members of the value at expr
    ///   {"cmd":"log","since":0}            Unity log lines captured since index
    ///   {"cmd":"shot","path":"C:/.../a.png"}
    ///   {"cmd":"run","name":"estate.open", ...}   mod-registered dev commands
    /// </summary>
    internal static class AgentBridge
    {
        private class Request
        {
            public string Line;
            public string Reply;
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
        }

        private static TcpListener _listener;
        private static readonly ConcurrentQueue<Request> Inbox = new ConcurrentQueue<Request>();
        private static readonly Dictionary<string, Func<JObject, object>> Commands =
            new Dictionary<string, Func<JObject, object>>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<GameObject> LastOptions = new List<GameObject>();
        private static readonly List<string> LogLines = new List<string>();
        private const int MaxLogLines = 2000;
        private static int _logBase;

        public static bool Enabled => _listener != null;

        public static void Register(string name, Func<JObject, object> handler) => Commands[name] = handler;

        public static void Start(int port)
        {
            if (port <= 0 || _listener != null) return;
            Application.runInBackground = true;
            Application.logMessageReceivedThreaded += OnLog;
            _listener = new TcpListener(IPAddress.Loopback, port);
            _listener.Start();
            new Thread(AcceptLoop) { IsBackground = true, Name = "DD2Estate.AgentBridge" }.Start();
            Plugin.Log.LogInfo($"agent bridge listening on 127.0.0.1:{port}");
        }

        public static void Stop()
        {
            var l = _listener;
            _listener = null;
            try { l?.Stop(); } catch { }
            Application.logMessageReceivedThreaded -= OnLog;
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            lock (LogLines)
            {
                var line = type == LogType.Log ? condition : $"[{type}] {condition}";
                if (type == LogType.Exception || type == LogType.Error) line += "\n" + stackTrace;
                LogLines.Add(line);
                if (LogLines.Count > MaxLogLines)
                {
                    LogLines.RemoveRange(0, 500);
                    _logBase += 500;
                }
            }
        }

        private static void AcceptLoop()
        {
            while (_listener != null)
            {
                try
                {
                    var client = _listener.AcceptTcpClient();
                    new Thread(() => ClientLoop(client)) { IsBackground = true, Name = "DD2Estate.AgentBridge.Client" }.Start();
                }
                catch
                {
                    if (_listener == null) return;
                }
            }
        }

        private static void ClientLoop(TcpClient client)
        {
            try
            {
                using (client)
                using (var stream = client.GetStream())
                using (var reader = new StreamReader(stream, new UTF8Encoding(false)))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" })
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.Length == 0) continue;
                        var req = new Request { Line = line };
                        Inbox.Enqueue(req);
                        if (!req.Done.Wait(TimeSpan.FromSeconds(30)))
                            req.Reply = "{\"ok\":false,\"error\":\"timeout: main thread did not answer in 30s\"}";
                        writer.WriteLine(req.Reply);
                    }
                }
            }
            catch { }
        }

        /// <summary>Called from the plugin's Update on the main thread.</summary>
        public static void Pump()
        {
            while (Inbox.TryDequeue(out var req))
            {
                try
                {
                    var obj = JObject.Parse(req.Line);
                    var result = Handle(obj);
                    req.Reply = JsonConvert.SerializeObject(new { ok = true, result }, Formatting.None);
                }
                catch (Exception e)
                {
                    var inner = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;
                    req.Reply = JsonConvert.SerializeObject(new { ok = false, error = inner.GetType().Name + ": " + inner.Message, stack = inner.StackTrace });
                }
                req.Done.Set();
            }
        }

        private static object Handle(JObject o)
        {
            var cmd = (string)o["cmd"] ?? "";
            switch (cmd)
            {
                case "ping": return new { plugin = Plugin.Version, game = Application.version, frame = Time.frameCount };
                case "observe": return Observe((string)o["filter"], (bool?)o["all"] ?? false);
                case "click": return Click(o);
                case "tree": return Tree((string)o["path"], (int?)o["depth"] ?? 2);
                case "get": return Describe(Reflect.Eval((string)o["expr"]), (int?)o["depth"] ?? 1);
                case "members": return Reflect.Members(Reflect.Eval((string)o["expr"]), (string)o["filter"]);
                case "invoke": return Describe(Reflect.Invoke(Reflect.Eval((string)o["expr"]), (string)o["method"], o["args"] as JArray), (int?)o["depth"] ?? 1);
                case "set":
                    Reflect.Set(Reflect.Eval((string)o["expr"]), (string)o["member"], o["value"]);
                    return "ok";
                case "log": return Log((int?)o["since"] ?? -1, (string)o["filter"]);
                case "shot":
                    var path = (string)o["path"];
                    ScreenCapture.CaptureScreenshot(path);
                    return path;
                case "timescale":
                    if (o["value"] != null) Time.timeScale = (float)o["value"];
                    return Time.timeScale;
                case "run":
                    var name = (string)o["name"] ?? "";
                    if (!Commands.TryGetValue(name, out var handler))
                        throw new ArgumentException("unknown dev command '" + name + "'; known: " + string.Join(", ", Commands.Keys.OrderBy(k => k)));
                    return handler(o);
                case "help": return new { cmds = new[] { "ping", "observe", "click", "tree", "get", "members", "log", "shot", "timescale", "run" }, run = Commands.Keys.OrderBy(k => k).ToArray() };
                default: throw new ArgumentException("unknown cmd '" + cmd + "'");
            }
        }

        // ---- observe / click -------------------------------------------------------------------

        private static object Observe(string filter, bool all)
        {
            LastOptions.Clear();
            var scenes = new List<string>();
            for (var i = 0; i < SceneManager.sceneCount; i++) scenes.Add(SceneManager.GetSceneAt(i).name);

            var seen = new HashSet<GameObject>();
            var options = new List<object>();
            var es = EventSystem.current;
            var hits = new List<RaycastResult>();
            foreach (var mb in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
            {
                if (!(mb is IPointerClickHandler) && !(mb is ISubmitHandler) && !(mb is IPointerDownHandler)) continue;
                if (!mb.isActiveAndEnabled) continue;
                var go = mb.gameObject;
                if (!seen.Add(go)) continue;
                var selectable = go.GetComponent<Selectable>();
                var interactable = (selectable == null || selectable.IsInteractable()) && GroupsAllowInput(go.transform);
                var label = LabelOf(go);
                var path = PathOf(go.transform);
                if (!string.IsNullOrEmpty(filter) &&
                    path.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                    label.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;

                var pos = ScreenPos(go);
                var top = false;
                if (es != null && pos.HasValue)
                {
                    hits.Clear();
                    es.RaycastAll(new PointerEventData(es) { position = pos.Value }, hits);
                    top = hits.Count > 0 && (hits[0].gameObject == go || hits[0].gameObject.transform.IsChildOf(go.transform));
                }
                if (!all && (!interactable || !top)) continue;

                LastOptions.Add(go);
                options.Add(new
                {
                    id = LastOptions.Count - 1,
                    label,
                    type = mb.GetType().Name,
                    x = pos.HasValue ? (int)pos.Value.x : -1,
                    y = pos.HasValue ? (int)pos.Value.y : -1,
                    interactable,
                    top,
                    path
                });
                if (options.Count >= 300) break;
            }
            return new { scenes, screen = new[] { Screen.width, Screen.height }, timeScale = Time.timeScale, options };
        }

        private static object Click(JObject o)
        {
            GameObject go;
            if (o["id"] != null)
            {
                var id = (int)o["id"];
                if (id < 0 || id >= LastOptions.Count) throw new ArgumentException("id out of range of the last observe");
                go = LastOptions[id];
            }
            else
            {
                go = FindObject((string)o["path"]);
            }
            if (go == null) throw new ArgumentException("target not found (destroyed since observe?)");

            var es = EventSystem.current;
            var pos = ScreenPos(go) ?? new Vector2(Screen.width / 2f, Screen.height / 2f);
            var ped = new PointerEventData(es) { position = pos, pressPosition = pos, button = PointerEventData.InputButton.Left, clickCount = 1, pointerPress = go, pointerEnter = go };
            ExecuteEvents.ExecuteHierarchy(go, ped, ExecuteEvents.pointerEnterHandler);
            ExecuteEvents.ExecuteHierarchy(go, ped, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.ExecuteHierarchy(go, ped, ExecuteEvents.pointerUpHandler);
            var handled = ExecuteEvents.ExecuteHierarchy(go, ped, ExecuteEvents.pointerClickHandler);
            if (handled == null) handled = ExecuteEvents.ExecuteHierarchy(go, new BaseEventData(es), ExecuteEvents.submitHandler);
            return new { clicked = PathOf(go.transform), handledBy = handled != null ? handled.name : null };
        }

        private static bool GroupsAllowInput(Transform t)
        {
            for (; t != null; t = t.parent)
            {
                var g = t.GetComponent<CanvasGroup>();
                if (g == null) continue;
                if (!g.interactable || !g.blocksRaycasts) return false;
                if (g.ignoreParentGroups) break;
            }
            return true;
        }

        private static string LabelOf(GameObject go)
        {
            foreach (var t in go.GetComponentsInChildren<TMP_Text>(false))
                if (!string.IsNullOrWhiteSpace(t.text)) return Clean(t.text);
            foreach (var t in go.GetComponentsInChildren<Text>(false))
                if (!string.IsNullOrWhiteSpace(t.text)) return Clean(t.text);
            return go.name;
        }

        private static string Clean(string s)
        {
            s = s.Replace("\n", " ").Replace("\r", " ").Trim();
            return s.Length > 80 ? s.Substring(0, 80) + "…" : s;
        }

        private static Vector2? ScreenPos(GameObject go)
        {
            var rt = go.transform as RectTransform;
            if (rt == null)
            {
                var cam = Camera.main;
                if (cam == null) return null;
                var p = cam.WorldToScreenPoint(go.transform.position);
                return p.z < 0 ? (Vector2?)null : new Vector2(p.x, p.y);
            }
            var canvas = go.GetComponentInParent<Canvas>();
            var uiCam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            return RectTransformUtility.WorldToScreenPoint(uiCam, (corners[0] + corners[2]) * 0.5f);
        }

        internal static string PathOf(Transform t)
        {
            var parts = new List<string>();
            for (; t != null; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        internal static GameObject FindObject(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var go = GameObject.Find(path);
            if (go != null) return go;
            // GameObject.Find skips inactive objects and DontDestroyOnLoad roots can be awkward: fall back to a scan.
            foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
                if (t.gameObject.scene.IsValid() && PathOf(t) == path) return t.gameObject;
            return null;
        }

        // ---- tree ------------------------------------------------------------------------------

        private static object Tree(string path, int depth)
        {
            var sb = new StringBuilder();
            if (string.IsNullOrEmpty(path))
            {
                var roots = new List<GameObject>();
                for (var i = 0; i < SceneManager.sceneCount; i++)
                {
                    var scene = SceneManager.GetSceneAt(i);
                    sb.Append("# scene ").Append(scene.name).Append('\n');
                    foreach (var r in scene.GetRootGameObjects()) Dump(r.transform, 0, depth, sb);
                }
                // DontDestroyOnLoad roots live in a scene that SceneManager does not enumerate.
                sb.Append("# DontDestroyOnLoad\n");
                foreach (var r in DontDestroyRoots()) Dump(r.transform, 0, depth, sb);
            }
            else
            {
                var go = FindObject(path);
                if (go == null) throw new ArgumentException("not found: " + path);
                Dump(go.transform, 0, depth, sb);
            }
            return sb.ToString();
        }

        private static GameObject[] DontDestroyRoots()
        {
            var probe = new GameObject("DD2Estate.Probe");
            UnityEngine.Object.DontDestroyOnLoad(probe);
            var roots = probe.scene.GetRootGameObjects().Where(r => r != probe).ToArray();
            UnityEngine.Object.Destroy(probe);
            return roots;
        }

        private static void Dump(Transform t, int level, int depth, StringBuilder sb)
        {
            sb.Append(' ', level * 2).Append(t.gameObject.activeInHierarchy ? "" : "(off) ").Append(t.name).Append("  [");
            sb.Append(string.Join(",", t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name)));
            sb.Append("]");
            var text = t.GetComponent<TMP_Text>();
            if (text != null && !string.IsNullOrWhiteSpace(text.text)) sb.Append(" \"").Append(Clean(text.text)).Append('"');
            sb.Append('\n');
            if (level >= depth)
            {
                if (t.childCount > 0) sb.Append(' ', (level + 1) * 2).Append("… ").Append(t.childCount).Append(" children\n");
                return;
            }
            for (var i = 0; i < t.childCount; i++) Dump(t.GetChild(i), level + 1, depth, sb);
        }

        // ---- log -------------------------------------------------------------------------------

        private static object Log(int since, string filter)
        {
            lock (LogLines)
            {
                var end = _logBase + LogLines.Count;
                var from = since < 0 ? Math.Max(_logBase, end - 40) : Math.Max(_logBase, since);
                var lines = new List<string>();
                for (var i = from; i < end; i++)
                {
                    var l = LogLines[i - _logBase];
                    if (string.IsNullOrEmpty(filter) || l.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) lines.Add(l);
                }
                return new { next = end, lines };
            }
        }

        // ---- reflection reads ------------------------------------------------------------------

        internal static object Describe(object value, int depth)
        {
            if (value == null) return null;
            var type = value.GetType();
            if (type.IsPrimitive || value is string || value is decimal) return value;
            if (type.IsEnum) return value.ToString();
            if (value is UnityEngine.Object uo) return uo == null ? "(destroyed)" : $"{type.Name}:{uo.name}";
            if (value is IDictionary dict)
            {
                var d = new Dictionary<string, object>();
                var n = 0;
                foreach (DictionaryEntry e in dict)
                {
                    if (n++ >= 60) { d["…"] = dict.Count + " entries"; break; }
                    d[Convert.ToString(e.Key)] = depth > 0 ? Describe(e.Value, depth - 1) : Short(e.Value);
                }
                return d;
            }
            if (value is IEnumerable en)
            {
                var list = new List<object>();
                foreach (var item in en)
                {
                    if (list.Count >= 60) { list.Add("…"); break; }
                    list.Add(depth > 0 ? Describe(item, depth - 1) : Short(item));
                }
                return list;
            }
            if (depth <= 0) return Short(value);
            var result = new Dictionary<string, object> { ["$type"] = type.FullName };
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var f in type.GetFields(flags))
            {
                try { result[f.Name] = Describe(f.GetValue(value), depth - 1); }
                catch (Exception e) { result[f.Name] = "!" + e.GetType().Name; }
            }
            foreach (var p in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (p.GetIndexParameters().Length > 0 || !p.CanRead || result.ContainsKey(p.Name)) continue;
                try { result[p.Name] = Describe(p.GetValue(value, null), depth - 1); }
                catch (Exception e) { result[p.Name] = "!" + (e.InnerException ?? e).GetType().Name; }
            }
            return result;
        }

        private static string Short(object v)
        {
            if (v == null) return "null";
            if (v is UnityEngine.Object uo) return uo == null ? "(destroyed)" : $"{v.GetType().Name}:{uo.name}";
            var s = v.ToString();
            return s.Length > 120 ? s.Substring(0, 120) + "…" : s;
        }
    }
}
