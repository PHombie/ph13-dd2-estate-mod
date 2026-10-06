using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using DD2Estate.Dd1;
using DD2Estate.Dev;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD2Estate.Estate
{
    /// <summary>
    /// DD1's town by time of day. What DD1 does, and where it says so:
    /// <list type="bullet">
    /// <item>campaign/town/town_render_data.json gives the town "display states"; the regular one has four
    /// colour grades in its time_of_day_data: colours/town_screen_colour_morning.png, _afternoon, _evening and
    /// _night. Each is a 16x16x16 table as a picture 16 wide and 256 high (red across, green down a block of
    /// sixteen rows, the block is blue), the kind the dungeons have.</item>
    /// <item>shaders/town_post_effects.glsl draws the finished town scene (sky, backdrop, ground, buildings with
    /// their lights and smoke; nothing of the screen's frame, roster or bar) through two of the tables,
    /// texture3D(lut, rgb * 15/16 + 1/32), and mixes the two results by "TransitionTime".</item>
    /// <item>campaign/town/town.layout.darkest, time_of_day: time_per_grade 65, transition_time 2. The exe
    /// (TownDisplay's update, _windows/win32/Darkest.exe 0xa77800, and its draw, 0xa786fb): a clock runs from
    /// 0; the first table is the grade of the visit's index, the second the next one's (round and round),
    /// TransitionTime is (clock - 65) / 2 held to 0..1; once the clock passes 67 it starts again and the index
    /// moves on. Each time the town is loaded the index is drawn at random (0xa6fa18) and the clock is 0.</item>
    /// <item>The display state is chosen as the town is loaded (0xa80d60): the one named for the week's town
    /// event if there is one (the base game: Stress Relief's two, each with a background picture and one grade
    /// of its own), else the regular one. (The states "after a descent into the Darkest Dungeon" hang on DD1's
    /// own quest ids, which the estate's story does not have: left out.)</item>
    /// </list>
    /// Seen on real frames of the game: the hamlet at rest in three of the four grades; this file's table
    /// reading, applied to the town as the estate composes it, gives each of them to within two levels of 255
    /// (evening: a red dusk; night: blue-grey under a pale moon; afternoon: dull violet under a white sky).
    ///
    /// The estate's town is UI, which the game's pipeline has no colour grade for, and the mod ships no shader.
    /// So the grade is put into the town's pictures themselves: each is read once more from the DD1 install,
    /// every pixel is passed through the table (worker threads), and the result is copied over the picture the
    /// town already draws (a copy on the graphics card: nothing of the scene has to know). A picture's own
    /// transparent edges make the one difference to grading the finished frame, and none where it is solid.
    /// DD1's two seconds from one grade to the next are walked in as many steps as the machine manages (each
    /// step is a pass over all the pictures, prepared aside and swapped in one frame). The grade of the next
    /// visit is drawn and put in while the hamlet is away, so the town comes up in it.
    /// </summary>
    [EstateModule]
    internal static class TownTime
    {
        private const string LayoutFile = "campaign/town/town.layout.darkest";
        private const string RenderData = "campaign/town/town_render_data.json";
        private const string StockBackground = "campaign/town/town_bg.png";
        // FALLBACK: DD1's own values and tables, for an install whose files cannot be read.
        private const float StockSecondsPerGrade = 65f, StockTransitionSeconds = 2f;
        private static readonly string[] StockGrades =
        {
            "colours/town_screen_colour_morning.png", "colours/town_screen_colour_afternoon.png",
            "colours/town_screen_colour_evening.png", "colours/town_screen_colour_night.png"
        };
        private const int Size = 16;                 // a table's edge
        private const int Steps = 16;                // a transition is told apart in sixteenths
        private const string Ungraded = "none";
        /// <summary>Milliseconds a frame may spend handing finished pictures to the graphics card.</summary>
        public static float UploadBudget = 4f;

        /// <summary>DD1's hamlet by time of day ([Rules] TownTimeOfDay of the config); off, the town is as its art is.</summary>
        public static bool Enabled = true;

        private sealed class State
        {
            public string Name, Event, Background;
            public readonly List<string> Grades = new List<string>();
        }

        private sealed class Art
        {
            public Texture2D Texture;
            /// <summary>The picture's file in the DD1 install; for a piece of a skeleton's sheet that lies turned on it, the sheet's.</summary>
            public string File;
            /// <summary>The sheet's atlas and the piece's name, for such a piece; null for a whole file.</summary>
            public string Atlas, Region;
            public Color32[] Original, Output;
            /// <summary>What a pass has graded for the picture and not yet handed over: its buffer, or its own pixels.</summary>
            public Color32[] Ready;
            /// <summary>What the picture on the card is graded with now; null: as the game loaded it.</summary>
            public string Holds;
            public bool Broken;
        }

        private static readonly List<State> States = new List<State>();
        private static bool _rulesRead;
        private static float _secondsPerGrade = StockSecondsPerGrade, _transitionSeconds = StockTransitionSeconds;
        private static readonly Dictionary<string, float[]> Tables = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);
        private static readonly int[] Cell = new int[256];
        private static readonly float[] Share = new float[256];

        private static readonly Dictionary<int, Art> Arts = new Dictionary<int, Art>();
        private static readonly List<Graphic> Found = new List<Graphic>();
        private static readonly List<int> Dead = new List<int>();
        private static Runner _runner;
        private static RectTransform _town;
        private static State _state;
        private static int _index;
        private static float _clock, _nextPurge;
        private static bool _visiting, _prepared, _busy, _failed;
        private static int _passes, _generation;
        private static string _lastPass = "none yet";

        private sealed class Sheet
        {
            public Color32[] Pixels;
            public int Width, Height;
        }

        // sheets read for their turned pieces, kept while a round of reading lasts
        private static readonly Dictionary<string, Sheet> Sheets = new Dictionary<string, Sheet>(StringComparer.OrdinalIgnoreCase);

        private static void Register()
        {
            for (var v = 0; v < 256; v++)
            {
                var at = v * (Size - 1) / 255f;
                Cell[v] = Mathf.Min(Size - 2, (int)at);
                Share[v] = at - Cell[v];
            }
            if ((SystemInfo.copyTextureSupport & UnityEngine.Rendering.CopyTextureSupport.Basic) == 0)
            {
                Plugin.Log.LogInfo("Town time: this graphics card cannot copy one picture over another; the town is not regraded");
                _failed = true;
            }
            var go = new GameObject("DD2Estate.TownTime") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(go);
            _runner = go.AddComponent<Runner>();

            // The state: grade, clock, what the pictures hold. {"set":"night"} (or 0..3) puts a grade in at once,
            // clock at 0; {"seconds":63} sets the clock (the change begins at 65 and takes 2); {"next":true} starts the
            // change now; {"on":false} takes the grade off the town (and {"on":true} puts it back).
            AgentBridge.Register("town.time", o =>
            {
                ReadRules();
                if (o["on"] != null) Enabled = (bool)o["on"];
                if (o["set"] != null)
                {
                    var state = _state ?? Pick();
                    var wanted = (string)o["set"];
                    var index = -1;
                    for (var i = 0; i < state.Grades.Count; i++)
                        if (NameOf(state.Grades[i]) == wanted) index = i;
                    if (index < 0 && int.TryParse(wanted, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number >= 0 && number < state.Grades.Count) index = number;
                    if (index < 0) return "no such grade: " + string.Join(", ", state.Grades.ConvertAll(NameOf));
                    _state = state;
                    _index = index;
                    _clock = 0f;
                    _generation++;
                    if (_visiting) Bring(true);
                }
                if (o["seconds"] != null) _clock = Mathf.Clamp((float)o["seconds"], 0f, _secondsPerGrade + _transitionSeconds);
                if ((bool?)o["next"] ?? false) _clock = _secondsPerGrade;
                if (o["on"] != null)
                {
                    _generation++;
                    if (_visiting) Bring(true);
                }
                return Describe();
            });
        }

        /// <summary>HamletScene's one call: the town's root, once everything of the town hangs on it.</summary>
        public static void Attach(RectTransform town)
        {
            if (town == null || town.GetComponent<Mark>() != null) return;
            town.gameObject.AddComponent<Mark>();
        }

        // ---- DD1's rules ------------------------------------------------------------------------------------

        private static void ReadRules()
        {
            if (_rulesRead) return;
            _rulesRead = true;
            try
            {
                var layout = DarkestFile.Load(LayoutFile);
                var times = layout?.Find("time_of_day");
                if (times != null)
                {
                    _secondsPerGrade = Mathf.Max(1f, times.Float("time_per_grade", 0, StockSecondsPerGrade));
                    _transitionSeconds = Mathf.Max(0f, times.Float("transition_time", 0, StockTransitionSeconds));
                }
                var text = Dd1Install.ReadText(RenderData);
                if (text != null && JObject.Parse(text)["display_states"] is JArray states)
                {
                    foreach (var entry in states)
                    {
                        var state = new State
                        {
                            Name = (string)entry["name"] ?? "",
                            Event = (string)entry["active_on_town_event"] ?? "",
                            Background = (string)entry["background_texture"]
                        };
                        // a state that waits for one of DD1's own quests is not the estate's
                        if (!string.IsNullOrEmpty((string)entry["active_on_plot_quest_return"])) continue;
                        if (entry["time_of_day_data"] is JArray grades)
                            foreach (var grade in grades)
                            {
                                var tint = (string)grade["tint"];
                                if (!string.IsNullOrEmpty(tint) && Dd1Install.Exists(tint)) state.Grades.Add(tint);
                            }
                        if (state.Grades.Count > 0) States.Add(state);
                    }
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Town time: DD1's files could not be read, its stock values stand: " + e.Message); }
            if (States.Find(s => s.Event.Length == 0) == null)
            {
                var regular = new State { Name = "regular", Event = "", Background = StockBackground };
                foreach (var grade in StockGrades)
                    if (Dd1Install.Exists(grade)) regular.Grades.Add(grade);
                States.Insert(0, regular);
            }
        }

        // DD1 0xa80d60: the state of the week's town event, else the regular one.
        private static State Pick()
        {
            ReadRules();
            string week = null;
            try { week = TownEvents.Current?.Id; }
            catch (Exception) { }
            State regular = null;
            foreach (var state in States)
            {
                if (state.Event.Length == 0) regular = regular ?? state;
                else if (state.Event == week) return state;
            }
            return regular;
        }

        private static string NameOf(string grade)
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(grade) ?? "";
            const string prefix = "town_screen_colour_";
            return name.StartsWith(prefix, StringComparison.Ordinal) ? name.Substring(prefix.Length) : name;
        }

        // DD1 draws the visit's first grade at random; the clock starts at 0.
        private static void Draw()
        {
            _state = Pick();
            _index = _state != null && _state.Grades.Count > 1 ? UnityEngine.Random.Range(0, _state.Grades.Count) : 0;
            _clock = 0f;
        }

        private static float Blend => _transitionSeconds > 0f ? Mathf.Clamp01((_clock - _secondsPerGrade) / _transitionSeconds) : _clock > _secondsPerGrade ? 1f : 0f;

        // What the town is to show now, as a name the pictures are marked with.
        private static string Wanted(out string first, out string second, out float blend)
        {
            first = second = null;
            blend = 0f;
            if (!Enabled || _failed || _state == null || _state.Grades.Count == 0) return Ungraded;
            var count = _state.Grades.Count;
            var a = _index % count;
            var b = (a + 1) % count;
            var step = count > 1 ? Mathf.RoundToInt(Blend * Steps) : 0;
            if (step >= Steps)
            {
                a = b;
                step = 0;
            }
            first = _state.Grades[a];
            if (step == 0) return first;
            second = _state.Grades[b];
            blend = step / (float)Steps;
            return first + ">" + second + "@" + step.ToString(CultureInfo.InvariantCulture);
        }

        // ---- a visit ----------------------------------------------------------------------------------------

        private static void VisitBegins(RectTransform town)
        {
            _town = town;
            _visiting = true;
            try
            {
                ReadRules();
                // the grade was drawn when the hamlet went away; a week whose event has a state of its own draws anew
                if (!_prepared || !ReferenceEquals(_state, Pick())) Draw();
                _prepared = false;
                _generation++;      // a pass begun for another picture of the town is not to land on this one
                Background();
                Bring(true);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Town time: the town could not be graded: " + e);
                _failed = true;
            }
        }

        private static void VisitEnds()
        {
            _visiting = false;
            _town = null;
            // the next visit's grade, put in while nobody looks
            try
            {
                Draw();
                _prepared = true;
                _generation++;
            }
            catch (Exception e) { Plugin.Log.LogWarning("Town time: the next visit's grade could not be drawn: " + e.Message); }
        }

        // The state's own background picture (the two Stress Relief events have one each).
        private static void Background()
        {
            if (_town == null || _state == null) return;
            var image = _town.Find("Background") is Transform found ? found.GetComponent<Image>() : null;
            if (image == null) return;
            var file = Enabled && !string.IsNullOrEmpty(_state.Background) && Dd1Install.Exists(_state.Background) ? _state.Background : StockBackground;
            if (image.sprite != null && image.sprite.name == file) return;
            var sprite = Dd1Install.Sprite(file);
            if (sprite != null) image.sprite = sprite;
        }

        private static void Tick(float seconds)
        {
            if (_failed) return;
            if (_visiting && _state != null && Enabled)
            {
                // DD1 0xa77931: the clock runs to time_per_grade + transition_time, then the next grade is the first
                _clock += seconds;
                if (_clock > _secondsPerGrade + _transitionSeconds)
                {
                    _clock = 0f;
                    _index = (_index + 1) % Mathf.Max(1, _state.Grades.Count);
                }
            }
            if (_visiting) Bring(false);
            else if (!_busy) Follow();
            // after the pass for the next visit has been started: it takes the pixels the visit kept
            if (Time.unscaledTime >= _nextPurge)
            {
                _nextPurge = Time.unscaledTime + 2f;
                Purge();
            }
        }

        // ---- the town's pictures --------------------------------------------------------------------------------

        // Only what DD1 draws into its town scene: the pictures of campaign/town itself (sky, backdrop, ground,
        // cliff, bridge, tree, backgrounds) and the buildings' skeleton sheets. Not the name plate, not the alerts.
        private static bool OfTheScene(string file)
        {
            if (string.IsNullOrEmpty(file)) return false;
            if (file.StartsWith("fx/town_", StringComparison.OrdinalIgnoreCase)) return true;
            const string town = "campaign/town/";
            if (!file.StartsWith(town, StringComparison.OrdinalIgnoreCase)) return false;
            var rest = file.Substring(town.Length);
            return rest.IndexOf('/') < 0 || rest.StartsWith("sky/", StringComparison.OrdinalIgnoreCase);
        }

        private static void Collect()
        {
            if (_town == null) return;
            _town.GetComponentsInChildren(true, Found);
            foreach (var graphic in Found)
            {
                if (graphic == null || graphic is TMP_Text || graphic is TMP_SubMeshUI) continue;
                var texture = graphic.mainTexture as Texture2D;
                if (texture == null) continue;
                var id = texture.GetInstanceID();
                if (Arts.ContainsKey(id)) continue;
                var file = texture.name;
                if (!OfTheScene(file)) continue;
                // A piece that lies turned on its skeleton's sheet is a picture of its own, named "<atlas>/<piece>" (SpineAtlas).
                string atlas = null, region = null;
                const string mark = ".atlas/";
                var cut = file.IndexOf(mark, StringComparison.OrdinalIgnoreCase);
                if (cut > 0)
                {
                    atlas = file.Substring(0, cut + mark.Length - 1);
                    region = file.Substring(cut + mark.Length);
                    file = atlas.Substring(0, atlas.Length - ".atlas".Length) + ".png";
                }
                if (!Dd1Install.Exists(file)) continue;
                var name = graphic.transform.name;
                if (name.StartsWith("Alert", StringComparison.Ordinal) || UnderNamePlate(graphic.transform)) continue;
                var format = texture.format;
                var known = format == TextureFormat.RGBA32 || format == TextureFormat.ARGB32 || format == TextureFormat.RGB24;
                Arts[id] = new Art { Texture = texture, File = file, Atlas = atlas, Region = region, Broken = !known };
                if (!known) Plugin.Log.LogInfo("Town time: " + file + " is kept in a format that cannot be regraded (" + format + ")");
            }
            Found.Clear();
        }

        private static bool UnderNamePlate(Transform t)
        {
            for (; t != null && t != _town; t = t.parent)
                if (t.name == "NamePlate" || t.name.StartsWith("Alert.", StringComparison.Ordinal)) return true;
            return false;
        }

        private static void Purge()
        {
            Dead.Clear();
            foreach (var pair in Arts)
                if (pair.Value.Texture == null) Dead.Add(pair.Key);
            foreach (var id in Dead) Arts.Remove(id);
            // the tables' copies of the pixels are kept for the visit and the pass after it only
            if (!_visiting && !_busy)
                foreach (var art in Arts.Values) art.Original = art.Output = art.Ready = null;
            if (!_busy) Sheets.Clear();
        }

        /// <summary>
        /// The town on screen is brought to what it is to show. <paramref name="all"/>: every picture, now (the
        /// town is about to be seen). Otherwise a picture that is new on screen is graded at once, and a change
        /// of the grade is walked in aside (<see cref="Pass"/>).
        /// </summary>
        private static void Bring(bool all)
        {
            try
            {
                Collect();
                var key = Wanted(out var first, out var second, out var blend);
                float[] table = null;
                var stale = false;
                foreach (var art in Arts.Values)
                {
                    if (art.Broken || art.Texture == null || art.Holds == key) continue;
                    if (!all && art.Holds != null)
                    {
                        stale = true;
                        continue;
                    }
                    if (key == Ungraded && art.Holds == null)
                    {
                        art.Holds = key;        // as the game loaded it is what is wanted
                        continue;
                    }
                    if (table == null && key != Ungraded) table = Table(first, second, blend);
                    if (!Original(art)) continue;
                    // a pass aside may be writing the picture's own buffer on its threads: then this one has its own
                    Color32[] own = null;
                    var pixels = _busy ? Grade(art.Original, table, ref own) : Grade(art.Original, table, ref art.Output);
                    Put(art, Temporary(art, pixels));
                    art.Holds = key;
                }
                if (all && !_busy) Sheets.Clear();
                if (stale && !_busy) _runner.StartCoroutine(Pass(key, first, second, blend));
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Town time: grading the town failed, it is left as it is: " + e);
                _failed = true;
            }
        }

        // The hamlet is away: its pictures follow the grade drawn for the next visit.
        private static void Follow()
        {
            var key = Wanted(out var first, out var second, out var blend);
            foreach (var art in Arts.Values)
            {
                if (art.Broken || art.Texture == null || art.Holds == key || art.Holds == null && key == Ungraded) continue;
                _runner.StartCoroutine(Pass(key, first, second, blend));
                return;
            }
        }

        // One step of the grade for every picture that does not hold it: the pixels are read (a picture a frame),
        // graded on worker threads, handed to the card a few a frame, and swapped in together.
        private static IEnumerator Pass(string key, string first, string second, float blend)
        {
            _busy = true;
            var generation = _generation;
            var clock = Stopwatch.StartNew();
            var arts = new List<Art>();
            foreach (var art in Arts.Values)
                if (!art.Broken && art.Texture != null && art.Holds != key && !(art.Holds == null && key == Ungraded)) arts.Add(art);
            float[] table = null;
            try { table = key != Ungraded ? Table(first, second, blend) : null; }
            catch (Exception e) { Fail("its table", e); }
            if (_failed) yield break;

            foreach (var art in arts)
            {
                if (art.Original != null || art.Texture == null) continue;
                try { Original(art); }
                catch (Exception e) { Fail(art.File, e); }
                if (_failed) yield break;
                yield return null;
            }
            arts.RemoveAll(art => art.Original == null || art.Texture == null);
            Sheets.Clear();

            var work = Task.Run(() => Parallel.ForEach(arts, art => art.Ready = Grade(art.Original, table, ref art.Output)));
            while (!work.IsCompleted) yield return null;
            if (work.IsFaulted)
            {
                Fail("the worker threads", work.Exception);
                yield break;
            }

            var ready = new List<KeyValuePair<Art, Texture2D>>();
            var frame = Stopwatch.StartNew();
            foreach (var art in arts)
            {
                if (art.Texture == null) continue;
                Texture2D temporary = null;
                try { temporary = Temporary(art, art.Ready); }
                catch (Exception e) { Fail(art.File, e); }
                art.Ready = null;
                if (_failed) break;
                ready.Add(new KeyValuePair<Art, Texture2D>(art, temporary));
                if (frame.Elapsed.TotalMilliseconds < UploadBudget) continue;
                yield return null;
                frame.Restart();
            }
            foreach (var pair in ready)
            {
                try
                {
                    // the town was set to something else meanwhile (a new visit, the bridge): this step is dropped
                    if (!_failed && pair.Key.Texture != null && generation == _generation)
                    {
                        Put(pair.Key, pair.Value);
                        pair.Key.Holds = key;
                    }
                    else if (pair.Value != null) UnityEngine.Object.Destroy(pair.Value);
                }
                catch (Exception e) { Fail(pair.Key.File, e); }
            }
            _passes++;
            _lastPass = key + ": " + ready.Count + " pictures in " + clock.ElapsedMilliseconds + " ms";
            _busy = false;
        }

        private static void Fail(string what, Exception e)
        {
            Plugin.Log.LogError("Town time: " + what + " could not be graded, the town is left as it is: " + e);
            _failed = true;
            _busy = false;
        }

        // The picture's pixels as the game loaded them (the same file read the same way).
        private static bool Original(Art art)
        {
            if (art.Original != null) return true;
            var sheet = Pixels(art);
            if (sheet == null)
            {
                art.Broken = true;
                return false;
            }
            if (art.Atlas == null)
            {
                if (sheet.Width != art.Texture.width || sheet.Height != art.Texture.height) art.Broken = true;
                else art.Original = sheet.Pixels;
                return !art.Broken;
            }

            // The piece stood upright, exactly as SpineAtlas made its picture: upright pixel (x, y from the top) is
            // sheet pixel (X + y, Y + Width - 1 - x).
            var atlas = SpineAtlas.Load(art.Atlas);
            var r = atlas?.Find(art.Region);
            if (r == null || !r.Rotated || r.Width != art.Texture.width || r.Height != art.Texture.height
                || r.X < 0 || r.Y < 0 || r.X + r.Height > sheet.Width || r.Y + r.Width > sheet.Height)
            {
                art.Broken = true;
                return false;
            }
            var upright = new Color32[r.Width * r.Height];
            for (var y = 0; y < r.Height; y++)
            {
                var row = (r.Height - 1 - y) * r.Width;
                for (var x = 0; x < r.Width; x++)
                    upright[row + x] = sheet.Pixels[(sheet.Height - 1 - (r.Y + r.Width - 1 - x)) * sheet.Width + r.X + y];
            }
            art.Original = upright;
            return true;
        }

        // A whole file's pixels; a skeleton's sheet is kept for the pieces that are cut from it in the same round.
        private static Sheet Pixels(Art art)
        {
            var shared = art.File.StartsWith("fx/", StringComparison.OrdinalIgnoreCase);
            if (shared && Sheets.TryGetValue(art.File, out var kept)) return kept;
            var texture = Dd1Install.LoadTexture(art.File, true, false);
            if (texture == null) return null;
            try
            {
                var sheet = new Sheet { Pixels = texture.GetPixels32(), Width = texture.width, Height = texture.height };
                if (shared) Sheets[art.File] = sheet;
                return sheet;
            }
            finally { UnityEngine.Object.Destroy(texture); }
        }

        // DD1's shader for every pixel: the table read between its sixteen steps a side, the picture's alpha kept.
        // Returns the graded pixels: in <paramref name="buffer"/> (made or made anew when it does not fit), or the
        // picture's own pixels when there is no table (the town as its art is).
        private static Color32[] Grade(Color32[] from, float[] table, ref Color32[] buffer)
        {
            if (table == null) return from;
            if (buffer == null || buffer.Length != from.Length || ReferenceEquals(buffer, from)) buffer = new Color32[from.Length];
            var to = buffer;
            const int rows = 256;
            var count = (from.Length + rows - 1) / rows;
            Parallel.For(0, rows, row =>
            {
                var start = row * count;
                var end = Math.Min(from.Length, start + count);
                byte lastR = 0, lastG = 0, lastB = 0, outR = 0, outG = 0, outB = 0;
                var have = false;
                for (var i = start; i < end; i++)
                {
                    var p = from[i];
                    if (!have || p.r != lastR || p.g != lastG || p.b != lastB)
                    {
                        have = true;
                        lastR = p.r;
                        lastG = p.g;
                        lastB = p.b;
                        float fr = Share[p.r], fg = Share[p.g], fb = Share[p.b];
                        var at = ((Cell[p.b] * Size + Cell[p.g]) * Size + Cell[p.r]) * 3;
                        const int red = 3, green = Size * 3, blue = Size * Size * 3;
                        outR = Mix(table, at, red, green, blue, fr, fg, fb);
                        outG = Mix(table, at + 1, red, green, blue, fr, fg, fb);
                        outB = Mix(table, at + 2, red, green, blue, fr, fg, fb);
                    }
                    to[i] = new Color32(outR, outG, outB, p.a);
                }
            });
            return to;
        }

        private static byte Mix(float[] t, int at, int red, int green, int blue, float fr, float fg, float fb)
        {
            var c00 = t[at] + (t[at + red] - t[at]) * fr;
            var c10 = t[at + green] + (t[at + green + red] - t[at + green]) * fr;
            var c01 = t[at + blue] + (t[at + blue + red] - t[at + blue]) * fr;
            var c11 = t[at + blue + green] + (t[at + blue + green + red] - t[at + blue + green]) * fr;
            var c0 = c00 + (c10 - c00) * fg;
            var c1 = c01 + (c11 - c01) * fg;
            var value = c0 + (c1 - c0) * fb + 0.5f;
            return value <= 0f ? (byte)0 : value >= 255f ? (byte)255 : (byte)value;
        }

        // The graded pixels as a picture of the same kind as the one on screen.
        private static Texture2D Temporary(Art art, Color32[] pixels)
        {
            var live = art.Texture;
            var mips = live.mipmapCount > 1;
            var temporary = new Texture2D(live.width, live.height, live.format, mips) { name = "DD2Estate.TownGrade", hideFlags = HideFlags.HideAndDontSave };
            temporary.SetPixels32(pixels);
            temporary.Apply(mips, true);
            return temporary;
        }

        // Copied over the picture the town draws, on the graphics card.
        private static void Put(Art art, Texture2D temporary)
        {
            if (temporary == null) return;
            try { Graphics.CopyTexture(temporary, art.Texture); }
            finally { UnityEngine.Object.Destroy(temporary); }
        }

        // ---- DD1's tables ---------------------------------------------------------------------------------------

        // One table, or two mixed (the shader mixes what it reads from each, which is the same thing): 0..255.
        private static float[] Table(string first, string second, float blend)
        {
            var a = Read(first);
            if (second == null || blend <= 0f) return a;
            var b = Read(second);
            var mixed = new float[a.Length];
            for (var i = 0; i < mixed.Length; i++) mixed[i] = a[i] + (b[i] - a[i]) * blend;
            return mixed;
        }

        // DD1's file: 16 wide, 256 high; x is red, y (from the top) inside a block of 16 rows is green, the block is blue.
        private static float[] Read(string file)
        {
            if (Tables.TryGetValue(file, out var known)) return known;
            var texture = Dd1Install.LoadTexture(file, true, false);
            if (texture == null) throw new InvalidOperationException("DD1 has no " + file);
            try
            {
                if (texture.width != Size || texture.height != Size * Size) throw new InvalidOperationException(file + " is not a 16x256 table");
                var pixels = texture.GetPixels32();      // rows from the bottom
                var table = new float[Size * Size * Size * 3];
                for (var blue = 0; blue < Size; blue++)
                    for (var green = 0; green < Size; green++)
                        for (var red = 0; red < Size; red++)
                        {
                            var pixel = pixels[(Size * Size - 1 - (blue * Size + green)) * Size + red];
                            var at = ((blue * Size + green) * Size + red) * 3;
                            table[at] = pixel.r;
                            table[at + 1] = pixel.g;
                            table[at + 2] = pixel.b;
                        }
                Tables[file] = table;
                return table;
            }
            finally { UnityEngine.Object.Destroy(texture); }
        }

        // ---- dev bridge -----------------------------------------------------------------------------------------

        public static object Describe()
        {
            ReadRules();
            var key = Wanted(out _, out _, out var blend);
            int held = 0, pictures = 0, broken = 0;
            long pixels = 0;
            foreach (var art in Arts.Values)
            {
                if (art.Texture == null) continue;
                pictures++;
                if (art.Broken) broken++;
                else if (art.Holds == key || art.Holds == null && key == Ungraded) held++;
                pixels += (long)art.Texture.width * art.Texture.height;
            }
            return new
            {
                enabled = Enabled, failed = _failed, visiting = _visiting,
                state = _state?.Name,
                grades = _state != null ? _state.Grades.ConvertAll(NameOf) : null,
                index = _index,
                grade = _state != null && _state.Grades.Count > 0 ? NameOf(_state.Grades[_index % _state.Grades.Count]) : null,
                seconds = _clock, secondsPerGrade = _secondsPerGrade, transitionSeconds = _transitionSeconds,
                blend,
                wanted = key,
                pictures, holding = held, cannot = broken, megapixels = pixels / 1e6,
                busy = _busy, passes = _passes, lastPass = _lastPass,
                copy = SystemInfo.copyTextureSupport.ToString()
            };
        }

        /// <summary>On the town's root: tells when the hamlet comes and goes.</summary>
        private class Mark : MonoBehaviour
        {
            private void OnEnable() => VisitBegins((RectTransform)transform);

            private void OnDisable() => VisitEnds();
        }

        /// <summary>
        /// Outlives the hamlet: the clock, and the passes that run while the hamlet is away. It looks after every
        /// Update of the frame, so a picture the town has just been given (a building's new art) is graded before
        /// it is first drawn.
        /// </summary>
        private class Runner : MonoBehaviour
        {
            private void LateUpdate()
            {
                try { Tick(Time.unscaledDeltaTime); }
                catch (Exception e)
                {
                    Plugin.Log.LogError("Town time: " + e);
                    _failed = true;
                }
            }
        }
    }
}
