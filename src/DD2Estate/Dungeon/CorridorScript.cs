using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using DD2Estate.Core;
using DD2Estate.Dd1;
using UnityEngine;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// One of DD1's <c>scripts/timescript/*.times</c> files (docs/recon/dd1-corridor-rendering.md, section 9):
    /// commands run one after another; <c>group:</c> .. <c>end:</c> runs its commands together and ends with
    /// the longest; <c>tween: target[,component]</c> takes a property to a <c>.value</c>, to an <c>.expr</c>, or
    /// back to what it was before the script (<c>.track self</c>), at once or over <c>.time</c> seconds with an
    /// <c>.easing</c>; <c>lambda: name</c> calls into the game (in a group: after <c>.time</c>);
    /// <c>wait: seconds</c>; <c>lock:</c> / <c>unlock:</c> hold a property against the game's own changes.
    /// </summary>
    internal class TimeScript
    {
        public enum Kind { Tween, Group, Lambda, Wait, Other }

        public class Command
        {
            public Kind Kind;
            public string Target;          // "camera.zoom", "camera.absolute_focus,y", "actor.area_pos,x"; a lambda's name
            public bool HasValue, Track;
            public float Value, Time;
            public string Expr, Easing;
            public List<Command> Children;
        }

        public readonly List<Command> Commands = new List<Command>();
        private static readonly Dictionary<string, TimeScript> Cache = new Dictionary<string, TimeScript>();

        /// <summary>scripts/timescript/&lt;name&gt;.times of the DD1 install; an empty script when DD1 has none.</summary>
        public static TimeScript Load(string name)
        {
            if (Cache.TryGetValue(name, out var script)) return script;
            script = new TimeScript();
            try
            {
                var text = Dd1Install.ReadText("scripts/timescript/" + name + ".times");
                if (text != null) script.Parse(text);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Corridor: DD1's timescript " + name + " could not be read: " + e.Message);
            }
            return Cache[name] = script;
        }

        private void Parse(string text)
        {
            List<Command> group = null;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw;
                var comment = line.IndexOf("//", StringComparison.Ordinal);
                if (comment >= 0) line = line.Substring(0, comment);
                var tokens = Tokens(line);
                if (tokens.Count == 0) continue;
                var into = group ?? Commands;
                switch (tokens[0])
                {
                    case "group:":
                        group = new List<Command>();
                        Commands.Add(new Command { Kind = Kind.Group, Children = group });
                        break;
                    case "end:":
                        group = null;
                        break;
                    case "wait:":
                        into.Add(new Command { Kind = Kind.Wait, Time = tokens.Count > 1 ? Number(tokens[1]) : 0f });
                        break;
                    case "lambda:":
                        if (tokens.Count > 1) into.Add(Fields(new Command { Kind = Kind.Lambda, Target = tokens[1] }, tokens, 2));
                        break;
                    case "tween:":
                        if (tokens.Count > 1) into.Add(Fields(new Command { Kind = Kind.Tween, Target = tokens[1] }, tokens, 2));
                        break;
                    default:
                        into.Add(new Command { Kind = Kind.Other, Target = line.Trim() });      // lock:, unlock:
                        break;
                }
            }
        }

        private static Command Fields(Command command, List<string> tokens, int from)
        {
            for (var i = from; i + 1 < tokens.Count; i += 2)
            {
                var value = tokens[i + 1];
                switch (tokens[i])
                {
                    case ".value": command.HasValue = true; command.Value = Number(value); break;
                    case ".time": command.Time = Number(value); break;
                    case ".expr": command.Expr = value; break;
                    case ".track": command.Track = true; break;
                    case ".easing": command.Easing = value; break;
                }
            }
            return command;
        }

        private static float Number(string text) => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0f;

        // words, with a quoted expression as one
        private static List<string> Tokens(string line)
        {
            var tokens = new List<string>();
            var i = 0;
            while (i < line.Length)
            {
                if (char.IsWhiteSpace(line[i])) { i++; continue; }
                if (line[i] == '"')
                {
                    var close = line.IndexOf('"', i + 1);
                    if (close < 0) close = line.Length;
                    tokens.Add(line.Substring(i + 1, close - i - 1));
                    i = close + 1;
                    continue;
                }
                var start = i;
                while (i < line.Length && !char.IsWhiteSpace(line[i])) i++;
                tokens.Add(line.Substring(start, i - start));
            }
            return tokens;
        }

        /// <summary>A script's expression: numbers, names, + - * and brackets ("centre - offset * 0.6"). An unknown name is 0.</summary>
        public static float Evaluate(string expr, Dictionary<string, float> names)
        {
            var at = 0;
            return Sum(expr, ref at, names);
        }

        private static float Sum(string s, ref int at, Dictionary<string, float> names)
        {
            var value = Product(s, ref at, names);
            while (true)
            {
                Skip(s, ref at);
                if (at >= s.Length || (s[at] != '+' && s[at] != '-')) return value;
                var minus = s[at++] == '-';
                var next = Product(s, ref at, names);
                value = minus ? value - next : value + next;
            }
        }

        private static float Product(string s, ref int at, Dictionary<string, float> names)
        {
            var value = Atom(s, ref at, names);
            while (true)
            {
                Skip(s, ref at);
                if (at >= s.Length || s[at] != '*') return value;
                at++;
                value *= Atom(s, ref at, names);
            }
        }

        private static float Atom(string s, ref int at, Dictionary<string, float> names)
        {
            Skip(s, ref at);
            if (at >= s.Length) return 0f;
            if (s[at] == '(')
            {
                at++;
                var inner = Sum(s, ref at, names);
                Skip(s, ref at);
                if (at < s.Length && s[at] == ')') at++;
                return inner;
            }
            if (s[at] == '-')
            {
                at++;
                return -Atom(s, ref at, names);
            }
            var start = at;
            while (at < s.Length && (char.IsLetterOrDigit(s[at]) || s[at] == '_' || s[at] == '.')) at++;
            var word = s.Substring(start, at - start);
            if (word.Length == 0) { at++; return 0f; }
            if (float.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return number;
            return names != null && names.TryGetValue(word, out var named) ? named : 0f;
        }

        private static void Skip(string s, ref int at)
        {
            while (at < s.Length && char.IsWhiteSpace(s[at])) at++;
        }
    }

    /// <summary>Settings of the scripted moments (investigating, traps) and of the camera's drift.</summary>
    internal static class CorridorScript
    {
        /// <summary>Off: the hooks do nothing.</summary>
        public static bool Enabled = true;
        /// <summary>DD1 blurs the scene behind the acting hero (layers.A_blurriness): the pipeline's gaussian depth of
        /// field on the main camera, whose picture has nothing in its depth buffer and so blurs as a whole.</summary>
        public static bool Blur = true;
        /// <summary>The pipeline's blur radius for one unit of DD1's blurriness (DD1: 3 at a curio, 5 at a trap).</summary>
        public static float BlurRadiusPerUnit = 0.3f;
        /// <summary>The scripts' "offset": the step between two places of a presentation; the acting hero goes to 0.6 of it
        /// before the scripts' "centre" (taken as the camera's x) and comes up to 0.5 of it. Not in any file
        /// [inferred]: below 0 the party's own spacing (area.actor_spacing) is used.</summary>
        public static float Offset = -1f;
        /// <summary>The acting hero and its curio are drawn by DD1's presentation camera (camera.darkest
        /// presentation_camera: z -1240) at the script's zoom: here they are scaled about the view's middle instead.</summary>
        public static bool Presentation = true;
        public static float PresentationZ = -1240f;
        /// <summary>A moment left open (an investigation never ended by the expedition) closes by itself after this many seconds.</summary>
        public static float AutoEnd = 6f;

        // ---- the steady cam (camera.darkest m_CameraParameters; DD1's executable 0x977be0) ----------------
        /// <summary>DD1's slow wander of the camera. </summary>
        public static bool Drift = true;
        /// <summary>1 is DD1; the offsets and the roll are multiplied by it.</summary>
        public static float DriftStrength = 1f;
        public static Vector3 TimeRange = new Vector3(2f, -0.5f, -1f);
        public static Vector3 MaxDistance = new Vector3(15f, 10f, 10f);
        public static Vector3 DriftSpeed = new Vector3(60f, 60f, 160f);
        public static Vector3 TurnRate = new Vector3(90f, 0f, 60f);
        public static float RollRange = 2f, Drag = 0.95f, PositionScalar = 1.2f;

        private static bool _loaded;

        public static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                foreach (var block in DD2Estate.Core.DarkestFile.Parse(Dd1Install.ReadText("scripts/camera.darkest")))
                {
                    if (block.Name == "m_CameraParameters")
                    {
                        TimeRange = V3(block, "SteadyCamTimeRange", TimeRange);
                        MaxDistance = V3(block, "SteadyCamMaxDistanceRange", MaxDistance);
                        DriftSpeed = V3(block, "SteadyCamDriftSpeed", DriftSpeed);
                        TurnRate = V3(block, "SteadyCamDriftTurnRate", TurnRate);
                        RollRange = (float)block.Number("SteadyCamRollRange", 0, RollRange);
                        Drag = (float)block.Number("SteadyCamMovementDrag", 0, Drag);
                        PositionScalar = (float)block.Number("SteadyCamPositionOffsetScalar", 0, PositionScalar);
                    }
                    else if (block.Name == "presentation_camera") PresentationZ = (float)block.Number("z_position", 0, PresentationZ);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Corridor: DD1's steady cam numbers could not be read, its shipped values are used: " + e.Message);
            }
        }

        private static Vector3 V3(DarkestBlock block, string field, Vector3 fallback)
        {
            return block.All(field).Count >= 3
                ? new Vector3((float)block.Number(field, 0, fallback.x), (float)block.Number(field, 1, fallback.y), (float)block.Number(field, 2, fallback.z))
                : fallback;
        }
    }

    internal partial class CorridorView
    {
        // ---- the scripts' properties ---------------------------------------------------------------------
        // what the scripts write; the view reads them every frame. At rest: the values DD1 starts from.
        private static readonly Dictionary<string, float> Rest = new Dictionary<string, float>
        {
            { "camera.zoom", 1f }, { "camera.absolute_focus,x", 0f }, { "camera.absolute_focus,y", 0f },
            { "camera.absolute_focus_blend,x", 0f }, { "camera.absolute_focus_blend,y", 0f },
            { "camera.shudder,x", 0f }, { "camera.shudder,y", 0f }, { "camera.tilt", 0f }, { "camera.foreground_active", 1f },
            { "layers.A_saturation", 1f }, { "layers.A_intensity", 1f }, { "layers.A_blurriness", 0f }, { "layers.B_intensity", 1f },
            { "layers.HUD_alpha", 1f }, { "presentation_camera.zoom", 1f }, { "presentation_camera.blend", 0f }, { "curio.zlayer", 0f },
            { "actor.area_pos,x", 0f }
        };

        private readonly Dictionary<string, float> _script = new Dictionary<string, float>(Rest);
        private readonly Dictionary<string, float> _scriptNames = new Dictionary<string, float>();
        private Coroutine _scriptRoutine;
        private int _scriptActor = -1;          // the slot of the hero the running script calls "actor"
        private float _scriptActorHome;         // where it stands in the party's line
        private CorridorProp _scriptProp;       // the script's "curio"
        private bool _scriptOpened, _scriptHeld;
        private float _scriptIdle, _shudderClock;
        private Vector3 _propHome;
        private float _propScale;

        /// <summary>A scripted moment is on screen (the acting hero stepped up, the scene blurred).</summary>
        public bool Presenting => _scriptActor >= 0 || _scriptRoutine != null;

        /// <summary>What DD1's scripts make of the panel (layers.HUD_alpha: 0 while a hero investigates). The mod's HUD is
        /// not the corridor's to hide: this is for it to read.</summary>
        public static float HudAlpha => Instance != null && Instance.isActiveAndEnabled ? Instance.Script("layers.HUD_alpha") : 1f;

        /// <summary>
        /// A blur of the scene asked for from outside the scripts, in their units (layers.A_blurriness): DD1 blurs
        /// the screen behind the choice of a completed quest (quest_info.complete_blur_time). 0 at rest.
        /// </summary>
        public static float ExtraBlur;

        private float Script(string name) => _script.TryGetValue(name, out var value) ? value : 0f;

        /// <summary>Dev bridge: the scripts' properties as they are now, and the hero of a slot.</summary>
        internal Dictionary<string, float> ScriptValues => _script;
        internal uint GuidOfSlot(int slot) => slot >= 0 && slot < _slotGuids.Count ? _slotGuids[slot] : 0u;
        internal int ScriptActor => _scriptActor;
        internal bool ScriptHeld => _scriptHeld;

        private static bool ScriptsOn => CorridorScript.Enabled && CorridorTransition.TimeScale > 0f && Instance != null && Instance.isActiveAndEnabled
            && Instance._scene.Root != null && !Instance.Transitioning;

        private static float ScriptDt => Time.deltaTime * Mathf.Max(0.01f, CorridorTransition.TimeScale);

        // ---- the hooks for the expedition ------------------------------------------------------------------

        /// <summary>
        /// A hero turns to the curio or obstacle the party stands at (DD1: investigate_intro): he steps up to it, the
        /// camera closes in, the scene behind blurs and loses its colour. Returns when DD1 opens the curio
        /// (open_lambda, a tenth of a second in); the hero's slow step goes on. <see cref="EndInteraction"/> ends it.
        /// Does nothing when the view is off, scripts are off or CorridorTransition.TimeScale is 0.
        /// </summary>
        /// <param name="at">The prop turned to, when the caller knows it (a trap is disarmed from the tile before its own).</param>
        public static IEnumerator Investigate(PropKind kind, uint heroGuid, CorridorProp at = null)
        {
            if (!ScriptsOn) yield break;
            var view = Instance;
            var prop = at ?? view.Props.Find(kind, view._isRoom ? -1 : view._currentTile) ?? view.Props.Find(kind);
            view.Play(kind == PropKind.Trap ? "disarm_intro" : "investigate_intro", view.SlotOf(heroGuid), prop, hold: true);
            while (view != null && view._scriptRoutine != null && !view._scriptOpened) yield return null;
        }

        /// <summary>DD1's investigate_extro (disarm_extro after a trap): everything back in 0.3 s. Safe to call when nothing is open.</summary>
        public static IEnumerator EndInteraction()
        {
            var view = Instance;
            if (view == null || !view._scriptHeld || !view.isActiveAndEnabled) yield break;
            var trap = view._scriptProp != null && view._scriptProp.Kind == PropKind.Trap;
            view.Play(trap ? "disarm_extro" : "investigate_extro", view._scriptActor, view._scriptProp, hold: false);
            while (view != null && view._scriptRoutine != null) yield return null;
        }

        /// <summary>A trap goes off under a hero (DD1: trap.times, 2.2 s): the camera goes to him and shakes, the scene darkens and blurs.</summary>
        public static IEnumerator TrapSprung(uint heroGuid)
        {
            if (!ScriptsOn) yield break;
            var view = Instance;
            // trap.times presents the hero only: the trap itself stays in the scene, darkened and blurred with it
            view.Play("trap", view.SlotOf(heroGuid), null, hold: false);
            while (view != null && view._scriptRoutine != null) yield return null;
        }

        /// <summary>A hero disarms the trap the party stands at (DD1: disarm_intro, a second of work, disarm_extro).</summary>
        public static IEnumerator Disarm(uint heroGuid)
        {
            if (!ScriptsOn) yield break;
            var view = Instance;
            // disarm_intro has no "open" moment: the hook comes back when the script's second of stepping up is over
            var wait = Investigate(PropKind.Trap, heroGuid);
            while (wait.MoveNext()) yield return wait.Current;
            var end = EndInteraction();
            while (end.MoveNext()) yield return end.Current;
        }

        private int SlotOf(uint heroGuid)
        {
            var slot = _slotGuids.IndexOf(heroGuid);
            return slot >= 0 ? slot : 0;
        }

        // ---- running a script ------------------------------------------------------------------------------

        private void Play(string name, int actor, CorridorProp prop, bool hold)
        {
            if (_scriptRoutine != null) StopCoroutine(_scriptRoutine);
            if (_scriptActor < 0)
            {
                // a new moment: the actor's place in the line is what "track self" goes back to
                _scriptActor = Mathf.Clamp(actor, 0, Mathf.Max(0, _slots.Count - 1));
                _scriptActorHome = _leadX - _scriptActor * _spacing;
                _script["actor.area_pos,x"] = _scriptActorHome;
                SetScriptProp(prop);
            }
            _scriptOpened = false;
            _scriptIdle = 0f;
            // DD1's names: "centre" is the middle of the view (the camera's x before the script moves it), "offset" the
            // step between two places: the acting hero comes to stand half a step before the middle, at the party's front
            _scriptNames["offset"] = CorridorScript.Offset >= 0f ? CorridorScript.Offset : CorridorNumbers.ActorSpacing;
            if (!_scriptNames.ContainsKey("centre") || !_scriptHeld) _scriptNames["centre"] = _isRoom ? CorridorNumbers.RoomCamera.x : _leadX + CorridorNumbers.Offset.x;
            _scriptNames["actor_x"] = _scriptActorHome;
            _scriptHeld = hold;
            _scriptRoutine = StartCoroutine(Run(TimeScript.Load(name), hold));
        }

        private void SetScriptProp(CorridorProp prop)
        {
            RestoreScriptProp();
            _scriptProp = prop;
            if (prop?.Model == null) return;
            _propHome = prop.Model.Root.localPosition;
            _propScale = prop.UnitsPerPixel;
        }

        private void RestoreScriptProp()
        {
            if (_scriptProp?.Model != null && _scriptProp.Model.Root != null)
            {
                _scriptProp.Model.Root.localPosition = _propHome;
                _scriptProp.Model.Root.localScale = new Vector3(_propScale, _propScale, 1f);
                _scriptProp.Model.SetLayer(0);
            }
            _scriptProp = null;
        }

        private IEnumerator Run(TimeScript script, bool hold)
        {
            foreach (var command in script.Commands)
            {
                switch (command.Kind)
                {
                    case TimeScript.Kind.Wait:
                        for (var t = 0f; t < command.Time; t += ScriptDt) yield return null;
                        break;
                    case TimeScript.Kind.Lambda:
                        Call(command.Target);
                        break;
                    case TimeScript.Kind.Tween:
                    case TimeScript.Kind.Group:
                        var tweens = command.Kind == TimeScript.Kind.Group ? command.Children : new List<TimeScript.Command> { command };
                        var from = new float[tweens.Count];
                        var to = new float[tweens.Count];
                        var called = new bool[tweens.Count];
                        var longest = 0f;
                        for (var i = 0; i < tweens.Count; i++)
                        {
                            var tween = tweens[i];
                            longest = Mathf.Max(longest, tween.Time);
                            if (tween.Kind != TimeScript.Kind.Tween) continue;
                            from[i] = Script(tween.Target);
                            to[i] = tween.Track ? Home(tween.Target) : tween.Expr != null ? TimeScript.Evaluate(tween.Expr, _scriptNames) : tween.Value;
                        }
                        for (var t = 0f; ; t += ScriptDt)
                        {
                            for (var i = 0; i < tweens.Count; i++)
                            {
                                var tween = tweens[i];
                                if (tween.Kind == TimeScript.Kind.Lambda)
                                {
                                    if (!called[i] && t >= tween.Time) { called[i] = true; Call(tween.Target); }
                                    continue;
                                }
                                if (tween.Kind != TimeScript.Kind.Tween || !Rest.ContainsKey(tween.Target)) continue;
                                var share = tween.Time <= 0f ? 1f : Mathf.Clamp01(t / tween.Time);
                                _script[tween.Target] = from[i] + (to[i] - from[i]) * Ease(tween.Easing, share);
                            }
                            if (t >= longest) break;
                            yield return null;
                        }
                        break;
                }
            }
            _scriptRoutine = null;
            if (!hold) ResetScript();
        }

        // what a property goes back to with ".track self"
        private float Home(string name)
        {
            if (name == "actor.area_pos,x") return _scriptActorHome;
            return Rest.TryGetValue(name, out var value) ? value : 0f;
        }

        private void Call(string lambda)
        {
            // hero_prep: DD1's hero takes its investigating pose (DD2's models have none);
            // open_lambda: DD1 opens the curio and shows what was found: the expedition does that when the hook returns
            if (lambda == "open_lambda") _scriptOpened = true;
        }

        private static float Ease(string name, float t)
        {
            switch (name)
            {
                case "easeOutSine": return Mathf.Sin(t * Mathf.PI * 0.5f);
                case "easeOutQuad": return 1f - (1f - t) * (1f - t);
                case "easeOutBack":
                    const float c1 = 1.70158f, c3 = c1 + 1f;
                    return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
                default: return t;
            }
        }

        /// <summary>Everything the scripts changed is as it was, at once (a fight, another area, the end of a moment).</summary>
        public void ResetScript()
        {
            if (_scriptRoutine != null) StopCoroutine(_scriptRoutine);
            _scriptRoutine = null;
            foreach (var pair in Rest) _script[pair.Key] = pair.Value;
            RestoreScriptProp();
            _scriptActor = -1;
            _scriptHeld = false;
            _scriptOpened = false;
        }

        // Every frame: what the scripts' properties mean for the view.
        private void ApplyScript(bool moving)
        {
            // a moment the expedition never closed: when the party walks on, or after a while
            if (_scriptHeld && _scriptRoutine == null)
            {
                _scriptIdle += Time.deltaTime;
                if (moving || _scriptIdle > CorridorScript.AutoEnd) StartCoroutine(EndInteraction());
            }
            // DD1 fades the strips with fade_controls' foreground_out_time / foreground_in_time. In the running
            // game a party that stands has no strips in front of it at all (2026-10-05: every frame taken a second
            // or more after it stopped, _lab/shots/cmp_top_right_full.png): they are there while it walks.
            var walking = !CorridorScene.ForegroundWhileWalking || Mathf.Abs(_speed) > 1f || _phase == Phase.WalkIn;
            var strips = walking && Script("camera.foreground_active") >= 0.5f ? 1f : 0f;
            var rate = strips > CorridorScene.ForegroundAlpha ? CorridorNumbers.ForegroundInTime : CorridorNumbers.ForegroundOutTime;
            CorridorScene.ForegroundAlpha = Mathf.MoveTowards(CorridorScene.ForegroundAlpha, strips, Time.deltaTime / Mathf.Max(0.01f, rate));
            CorridorLight.HeroBoost = Script("layers.B_intensity");
            CorridorPost.Scene(transform, Script("layers.A_saturation"), Script("layers.A_intensity"), CorridorScript.Blur ? Mathf.Max(Script("layers.A_blurriness"), ExtraBlur) : 0f);

            // the script's curio is drawn with the heroes (curio.zlayer 101) and through the presentation camera
            if (_scriptProp?.Model != null && _scriptProp.Model.Root != null)
            {
                var k = PresentationScale(_scriptProp.Foot.z);
                var pivot = new Vector3(CameraAt.x, CameraAt.y, _propHome.z);
                _scriptProp.Model.Root.localPosition = pivot + (_propHome - pivot) * k;
                _scriptProp.Model.Root.localScale = new Vector3(_propScale * k, _propScale * k, 1f);
                _scriptProp.Model.SetLayer(Script("curio.zlayer") > 0.5f ? LayerMask.NameToLayer("Characters") : 0);
            }
        }

        // DD1 draws the acting hero and its curio with the presentation camera (its own distance and zoom), blended in
        // by presentation_camera.blend: how much larger that is than the scene's camera shows a thing at depth z.
        private float PresentationScale(float z)
        {
            if (!CorridorScript.Presentation) return 1f;
            var blend = Mathf.Clamp01(Script("presentation_camera.blend"));
            if (blend <= 0f) return 1f;
            var shown = CameraZoom / Mathf.Max(1f, z - CameraAt.z);
            var presented = Script("presentation_camera.zoom") / Mathf.Max(1f, z - CorridorScript.PresentationZ);
            return Mathf.Lerp(1f, presented / shown, blend);
        }

        // ---- the steady cam (DD1's executable 0x977be0, spec 3.5) ------------------------------------------

        private Vector3 _driftOffset, _driftVelocity, _driftTarget;
        private float _driftTimer, _driftRoll, _driftRollTarget;

        /// <summary>The drift's offset now, DD1 units, and the roll in degrees (dev bridge).</summary>
        public Vector3 DriftOffset => _driftOffset;
        public float DriftRoll => _driftRoll;

        private void Drift(float dt, bool moving)
        {
            CorridorScript.Load();
            if (!CorridorScript.Drift || dt <= 0f)
            {
                _driftOffset = Vector3.MoveTowards(_driftOffset, Vector3.zero, 60f * dt);
                _driftVelocity = Vector3.zero;
                _driftRoll = 0f;
                return;
            }
            // how dark it is, squared, and how fast the party moves against its top speed (backwards counts double), squared
            var dark = 1f - CorridorLight.Torch / 100f;
            dark *= dark;
            var move = Mathf.Min(1f, Mathf.Abs(_speed) * (_speed < 0f ? 2f : 1f) / Mathf.Max(1f, CorridorNumbers.MaxForwardSpeed));
            move *= move;

            _driftTimer -= dt;
            if (_driftTimer < 0f)
            {
                // a new place to drift to: a random direction, scaled by the three limits; never above the camera's own
                // height, never nearer than its own distance
                _driftTimer = CorridorScript.TimeRange.x + move * CorridorScript.TimeRange.y + dark * CorridorScript.TimeRange.z;
                var a = UnityEngine.Random.value * 2f - 1f;
                var ring = Mathf.Sqrt(Mathf.Max(0f, 1f - a * a));
                var angle = UnityEngine.Random.value * Mathf.PI * 2f;
                var direction = new Vector3(Mathf.Cos(angle) * ring, Mathf.Sin(angle) * ring, Mathf.Abs(a)).normalized;
                _driftTarget = new Vector3(direction.x * CorridorScript.MaxDistance.x, -Mathf.Abs(direction.y * CorridorScript.MaxDistance.y), Mathf.Abs(direction.z * CorridorScript.MaxDistance.z));
                _driftRollTarget = UnityEngine.Random.Range(-CorridorScript.RollRange, CorridorScript.RollRange);
            }
            // DD1 writes (target - roll) * 0.8 * dt INTO the roll (not onto it): it stays a fraction of a degree
            _driftRoll = (_driftRollTarget - _driftRoll) * 0.8f * dt;

            var to = _driftTarget - _driftOffset;
            var toward = to.sqrMagnitude > 0f ? to.normalized : Vector3.zero;
            // DD1 applies the drag once a frame; its frames are sixtieths of a second
            _driftVelocity *= Mathf.Pow(CorridorScript.Drag, dt * 60f);
            var push = (CorridorScript.DriftSpeed.x + move * CorridorScript.DriftSpeed.y + dark * CorridorScript.DriftSpeed.z) * dt;
            if (_driftVelocity.sqrMagnitude <= 0f) _driftVelocity = toward * push;
            else
            {
                var heading = _driftVelocity.normalized;
                var along = Vector3.Dot(toward, heading);
                if (along >= 0f) _driftVelocity += toward * push;
                var turn = (CorridorScript.TurnRate.x + move * CorridorScript.TurnRate.y + dark * CorridorScript.TurnRate.z) * dt;
                if (Mathf.Cos(turn * Mathf.Deg2Rad) > along)
                {
                    // heading too far off the goal: the velocity is turned towards it
                    var axis = Vector3.Cross(heading, toward);
                    if (axis.sqrMagnitude > 1e-8f) _driftVelocity = Quaternion.AngleAxis(turn, axis.normalized) * _driftVelocity;
                }
            }
            _driftOffset += _driftVelocity * dt;
        }

        // The camera's shake of a script: camera.shudder,x is how far (pixels of DD1's view), ,y how fast (a second).
        private Vector2 Shudder()
        {
            var size = Script("camera.shudder,x");
            if (size <= 0.01f) return Vector2.zero;
            _shudderClock += Time.deltaTime * Mathf.Max(1f, Script("camera.shudder,y"));
            return new Vector2(Mathf.Sin(_shudderClock * 6.2832f), Mathf.Sin(_shudderClock * 6.2832f * 1.37f + 1.1f)) * size;
        }
    }
}
