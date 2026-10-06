using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Assets.Code.Actor;
using Assets.Code.Game;
using Assets.Code.Roster;
using Assets.Code.Utils;
using DD2Estate.Dev;
using DD2Estate.Estate;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// What can be seen of a hero model: its meshes one by one (the body, and every weapon and prop: DD2 names
    /// those msh_&lt;class&gt;_wpn_&lt;what&gt;), and for each whether it is drawn and lit, and if not, why not.
    ///
    /// A DD2 hero model carries every prop of the class at once (the Plague Doctor: dagger, flask, sack; the Grave
    /// Robber: dagger, pickaxe, bottle, three throwing daggers...). Which of them show is the animation's doing:
    /// clips switch the meshes' objects on and off. A mesh that shows is still not seen unless it is lit: the hero
    /// shader (Red Hook/Lit/Hero) draws black at _Brightness 0, which is what its materials hold; every scene of
    /// the game sets the value through MaterialPropertyBhv, a component on the model's root and one more on every
    /// weapon mesh, each of which hands its overrides to its own renderers when it sees them drawn.
    /// </summary>
    internal static class HeroParts
    {
        public class Part
        {
            public Renderer Renderer;
            public string Name, Path, Kind;
            public bool Weapon;
            public bool Active, Enabled, Visible, HasBlock, Instanced;
            public float Brightness = float.NaN;
            public string OffBy, Verdict;
            public string Bone, Hand;
            public float FromHand = float.NaN;
            public bool UnderPelvis, UnderHand;
            public int Bones;
            // the component that hands the scene's look to this renderer, and how it stands with it
            public MaterialPropertyBhv Holder;
            public string HolderPath;
            public bool HolderOn, HolderWaits, HolderApplied, HolderFirst;
            public int HolderFrame = -1, HolderList = -1;
            public string HolderOverrides, HolderParent;

            public bool Shown => Verdict == "shown";
        }

        private static readonly int BrightnessId = Shader.PropertyToID("_Brightness");
        private static readonly FieldInfo Waiting = AccessTools.Field(typeof(MaterialPropertyBhv), "m_PreviouslyInvisibleRenderers");
        private static readonly FieldInfo Applied = AccessTools.Field(typeof(MaterialPropertyBhv), "m_PreviouslyApplied");
        private static readonly FieldInfo LastFrame = AccessTools.Field(typeof(MaterialPropertyBhv), "m_LastFrameUpdated");
        private static readonly FieldInfo FirstFrame = AccessTools.Field(typeof(MaterialPropertyBhv), "m_FirstActiveFrame");
        private static MaterialPropertyBlock _block;

        public static bool IsWeapon(string name) => name.IndexOf("_wpn", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>The _Brightness a renderer is drawn with: its property block's, else NaN.</summary>
        public static float BrightnessOf(Renderer renderer, out bool hasBlock)
        {
            hasBlock = renderer.HasPropertyBlock();
            if (!hasBlock) return float.NaN;
            if (_block == null) _block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(_block);
            if (_block.HasFloat(BrightnessId)) return _block.GetFloat(BrightnessId);
            renderer.GetPropertyBlock(_block, 0);
            return _block.HasFloat(BrightnessId) ? _block.GetFloat(BrightnessId) : float.NaN;
        }

        /// <summary>Every mesh of the model, and what is to be seen of it.</summary>
        public static List<Part> Look(ActorBhv actor, bool holders)
        {
            var parts = new List<Part>();
            if (actor == null) return parts;
            var root = actor.transform;
            var scale = Mathf.Max(1e-4f, Mathf.Abs(root.lossyScale.y));
            var all = actor.GetComponentsInChildren<MaterialPropertyBhv>(true);
            Transform left = null, right = null;
            foreach (var bone in actor.GetComponentsInChildren<Transform>(false))
            {
                if (bone.name.EndsWith("l_Arm_WristSHJnt", StringComparison.Ordinal)) left = left ?? bone;
                else if (bone.name.EndsWith("r_Arm_WristSHJnt", StringComparison.Ordinal)) right = right ?? bone;
            }
            foreach (var renderer in actor.GetComponentsInChildren<Renderer>(true))
            {
                var skinned = renderer as SkinnedMeshRenderer;
                if (skinned == null && !(renderer is MeshRenderer)) continue;
                if (renderer.name.IndexOf("placeholder", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                var part = new Part
                {
                    Renderer = renderer, Name = renderer.name, Path = PathIn(root, renderer.transform), Kind = skinned != null ? "skinned" : "mesh",
                    Weapon = IsWeapon(renderer.name) || IsWeapon(renderer.transform.parent != null ? renderer.transform.parent.name : ""),
                    Active = renderer.gameObject.activeInHierarchy, Enabled = renderer.enabled, Visible = renderer.isVisible
                };
                if (!part.Active)
                    for (var t = renderer.transform; t != null && t != root.parent; t = t.parent)
                        if (!t.gameObject.activeSelf) { part.OffBy = t.name; break; }
                var shared = renderer.sharedMaterial;
                part.Instanced = shared != null && shared.name.EndsWith("(Instance)", StringComparison.Ordinal);
                part.Brightness = BrightnessOf(renderer, out part.HasBlock);
                // what it hangs on
                var carrier = skinned != null ? skinned.rootBone : renderer.transform.parent;
                if (skinned != null)
                {
                    part.Bones = skinned.bones.Length;
                    if (part.Bones > 0 && part.Bones <= 6) part.Bone = string.Join(",", skinned.bones.Where(b => b != null).Select(b => b.name).Distinct());
                    else part.Bone = (carrier != null ? carrier.name : "-") + " (root of " + part.Bones + ")";
                    if (part.Bones > 0 && part.Bones <= 6 && skinned.bones[0] != null) carrier = skinned.bones[0];
                }
                else part.Bone = carrier != null ? carrier.name : "-";
                for (var t = carrier; t != null && t != root; t = t.parent)
                {
                    if (t.name.EndsWith("ROOTSHJnt", StringComparison.Ordinal)) part.UnderPelvis = true;
                    if (t.name.EndsWith("Arm_WristSHJnt", StringComparison.Ordinal)) part.UnderHand = true;
                }
                if (part.Active && part.Enabled)
                {
                    var middle = renderer.bounds.center;
                    var toLeft = left != null ? (middle - left.position).magnitude : float.MaxValue;
                    var toRight = right != null ? (middle - right.position).magnitude : float.MaxValue;
                    if (toLeft < float.MaxValue || toRight < float.MaxValue)
                    {
                        part.Hand = toLeft <= toRight ? "left" : "right";
                        part.FromHand = Mathf.Min(toLeft, toRight) / scale;
                    }
                }
                foreach (var holder in all)
                {
                    if (!holder.IsRendererControlled(renderer)) continue;
                    part.Holder = holder;
                    break;
                }
                if (part.Holder != null && holders)
                {
                    var holder = part.Holder;
                    part.HolderPath = PathIn(root, holder.transform);
                    part.HolderOn = holder.isActiveAndEnabled;
                    try
                    {
                        part.HolderWaits = Waiting?.GetValue(holder) is List<Renderer> waits && waits.Contains(renderer);
                        part.HolderApplied = Applied?.GetValue(holder) is HashSet<Renderer> applied && applied.Contains(renderer);
                        part.HolderFrame = LastFrame?.GetValue(holder) is int frame ? frame : -1;
                        part.HolderFirst = FirstFrame?.GetValue(holder) is bool first && first;
                        part.HolderList = holder.CurrentRendererList != null ? holder.CurrentRendererList.Count : -1;
                        part.HolderOverrides = string.Join(",", holder.GetPropertyOverrideNames());
                        part.HolderParent = holder.Parent != null ? holder.Parent.name : null;
                    }
                    catch (Exception e) { part.HolderOverrides = "? " + e.Message; }
                }
                parts.Add(part);
            }

            // the light the body is drawn with: a mesh that shows without it is drawn black
            var body = float.NaN;
            var tallest = 0f;
            foreach (var part in parts)
            {
                if (part.Weapon || part.Kind != "skinned" || !part.Active || !part.Enabled || float.IsNaN(part.Brightness)) continue;
                var height = part.Renderer.bounds.size.y;
                if (height <= tallest) continue;
                tallest = height;
                body = part.Brightness;
            }
            foreach (var part in parts)
            {
                var renderer = part.Renderer;
                var material = renderer.sharedMaterial;
                if (!part.Active) part.Verdict = "off: the object " + (part.OffBy ?? "?") + " is switched off";
                else if (!part.Enabled) part.Verdict = "off: the renderer is disabled";
                else if (renderer.forceRenderingOff) part.Verdict = "off: rendering forced off";
                else if (renderer is SkinnedMeshRenderer mesh && mesh.sharedMesh == null) part.Verdict = "off: no mesh";
                else if (material == null) part.Verdict = "off: no material";
                else if (renderer.bounds.size.sqrMagnitude < 1e-10f) part.Verdict = "off: drawn in a point (scaled to nothing)";
                else if (!part.Visible) part.Verdict = "not drawn: no camera sees it";
                else if (!float.IsNaN(body) && body > 0.001f && material.HasProperty(BrightnessId) && (float.IsNaN(part.Brightness) || part.Brightness <= 0.001f) && material.GetFloat(BrightnessId) <= 0.001f)
                    part.Verdict = "unlit: drawn black (no _Brightness on it; the body has " + body.ToString("0.##") + ")";
                else part.Verdict = "shown";
            }
            return parts;
        }

        public static string PathIn(Transform root, Transform t)
        {
            var names = new List<string>();
            for (; t != null && t != root; t = t.parent) names.Add(t.name);
            names.Reverse();
            return string.Join("/", names);
        }

        /// <summary>What the model's animators play now.</summary>
        public static List<object> Animators(ActorBhv actor)
        {
            var list = new List<object>();
            foreach (var animator in actor.GetComponentsInChildren<Animator>(false))
            {
                if (animator.runtimeAnimatorController == null || animator.layerCount == 0) continue;
                var state = animator.GetCurrentAnimatorStateInfo(0);
                list.Add(new
                {
                    on = animator.name, controller = animator.runtimeAnimatorController.name, enabled = animator.enabled, speed = animator.speed,
                    state = state.shortNameHash, idle = state.IsName("idle_neutral"), time = state.normalizedTime, transition = animator.IsInTransition(0),
                    clips = animator.GetCurrentAnimatorClipInfo(0).Select(c => c.clip != null ? c.clip.name : "?").ToList(),
                    next = animator.IsInTransition(0) ? animator.GetNextAnimatorClipInfo(0).Select(c => c.clip != null ? c.clip.name : "?").ToList() : null
                });
            }
            return list;
        }

        private static string Clips(ActorBhv actor)
        {
            try
            {
                foreach (var animator in actor.GetComponentsInChildren<Animator>(false))
                {
                    if (animator.runtimeAnimatorController == null || animator.layerCount == 0) continue;
                    var names = animator.GetCurrentAnimatorClipInfo(0).Select(c => c.clip != null ? c.clip.name : "?");
                    return string.Join("+", names) + (animator.IsInTransition(0) ? " > " + string.Join("+", animator.GetNextAnimatorClipInfo(0).Select(c => c.clip != null ? c.clip.name : "?")) : "");
                }
            }
            catch (Exception) { }
            return "-";
        }

        // ---- the watch: when a part changes -----------------------------------------------------------------

        private static Coroutine _watch;
        private static readonly List<object> Changes = new List<object>();
        private static readonly Dictionary<int, string> Last = new Dictionary<int, string>();
        private static readonly HashSet<int> Known = new HashSet<int>();
        private const int MaxChanges = 3000;
        private static bool _watchAll;

        public static object Watch(bool? on, bool all, bool clear)
        {
            if (clear) Changes.Clear();
            if (on == true)
            {
                if (_watch != null) Plugin.Host.StopCoroutine(_watch);
                _watchAll = all;
                Last.Clear();
                Known.Clear();
                _watch = Plugin.Host.StartCoroutine(Watching());
            }
            else if (on == false && _watch != null)
            {
                Plugin.Host.StopCoroutine(_watch);
                _watch = null;
            }
            return new { on = _watch != null, frame = Time.frameCount, changes = Changes.Count };
        }

        public static List<object> Log => Changes;

        /// <summary>The hero models that stand anywhere now: the corridor's, a fight's, a camp's, the sheet's.</summary>
        public static List<ActorBhv> HeroModels()
        {
            var heroes = new HashSet<uint>();
            try
            {
                foreach (var guid in Singleton<GameTypeMgr>.Instance.RosterManager.GetActorGuids(RosterStatusType.PARTY)) heroes.Add(guid);
                foreach (var guid in RosterLifecycle.LivingGuids()) heroes.Add(guid);
            }
            catch (Exception) { }
            var list = new List<ActorBhv>();
            foreach (var actor in UnityEngine.Object.FindObjectsOfType<ActorBhv>())
                if (actor != null && heroes.Contains(actor.GetActorGuid())) list.Add(actor);
            return list;
        }

        private static IEnumerator Watching()
        {
            var end = new WaitForEndOfFrame();
            var models = new List<ActorBhv>();
            var next = 0f;
            while (true)
            {
                yield return end;
                if (Time.unscaledTime >= next)
                {
                    next = Time.unscaledTime + 0.25f;
                    models = HeroModels();
                }
                foreach (var actor in models)
                {
                    if (actor == null) continue;
                    List<Part> parts;
                    try { parts = Look(actor, false); }
                    catch (Exception) { continue; }
                    var fresh = Known.Add(actor.GetInstanceID());
                    foreach (var part in parts)
                    {
                        if (!_watchAll && !part.Weapon) continue;
                        var id = part.Renderer.GetInstanceID();
                        if (Last.TryGetValue(id, out var was) && was == part.Verdict) continue;
                        Last[id] = part.Verdict;
                        if (Changes.Count >= MaxChanges) continue;
                        Changes.Add(new
                        {
                            frame = Time.frameCount, time = Time.unscaledTime, mode = GameModeMgr.CurrentMode?.GetName(),
                            where = Where(actor), model = actor.name + "#" + actor.GetInstanceID(), guid = actor.GetActorGuid(),
                            part = part.Name, weapon = part.Weapon, was = fresh ? "(new model)" : was, now = part.Verdict, clips = Clips(actor)
                        });
                    }
                }
            }
        }

        public static string Where(ActorBhv actor)
        {
            var path = AgentBridge.PathOf(actor.transform);
            if (path.StartsWith("DD2Estate.Corridor", StringComparison.Ordinal)) return "corridor";
            if (path.StartsWith("Arena", StringComparison.Ordinal)) return "fight";
            if (path.StartsWith("Camp", StringComparison.Ordinal)) return "camp";
            var cut = path.IndexOf('/');
            return cut > 0 ? path.Substring(0, cut) : path;
        }

        // ---- the whole rig, to a file -----------------------------------------------------------------------

        public static JObject Rig(ActorBhv actor)
        {
            var root = actor.transform;
            var all = actor.GetComponentsInChildren<Transform>(true);
            var index = new Dictionary<Transform, int>();
            for (var i = 0; i < all.Length; i++) index[all[i]] = i;
            var transforms = new JArray();
            var special = new JArray();
            foreach (var t in all)
            {
                var components = new JArray();
                foreach (var component in t.GetComponents<Component>())
                {
                    if (component == null || component is Transform) continue;
                    var type = component.GetType();
                    components.Add(type.Name);
                    if (component is Renderer || component is Animator || component is MeshFilter || component is ParticleSystem || component is MaterialPropertyBhv) continue;
                    // whatever else sits on the rig: followers, constraints, bodies, the game's own behaviours
                    var entry = new JObject { ["t"] = index[t], ["type"] = type.FullName, ["enabled"] = !(component is Behaviour behaviour) || behaviour.enabled };
                    try
                    {
                        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                        {
                            if (field.IsNotSerialized) continue;
                            var value = field.GetValue(component);
                            if (value is Transform target) entry[field.Name] = target != null ? PathIn(root, target) : null;
                            else if (value is GameObject go) entry[field.Name] = go != null ? PathIn(root, go.transform) : null;
                            else if (value is Component other) entry[field.Name] = other != null ? other.GetType().Name + "@" + PathIn(root, other.transform) : null;
                            else if (value is string || value is bool || value is int || value is float || value is Enum) entry[field.Name] = value.ToString();
                            else if (value is Vector3 v) entry[field.Name] = v.x + "," + v.y + "," + v.z;
                        }
                    }
                    catch (Exception e) { entry["error"] = e.Message; }
                    special.Add(entry);
                }
                transforms.Add(new JObject
                {
                    ["i"] = index[t], ["p"] = t.parent != null && index.TryGetValue(t.parent, out var p) ? p : -1, ["n"] = t.name, ["a"] = t.gameObject.activeSelf, ["tag"] = t.tag == "Untagged" ? null : t.tag,
                    ["lp"] = new JArray(R(t.localPosition.x), R(t.localPosition.y), R(t.localPosition.z)),
                    ["lr"] = new JArray(R(t.localRotation.x), R(t.localRotation.y), R(t.localRotation.z), R(t.localRotation.w)),
                    ["ls"] = new JArray(R(t.localScale.x), R(t.localScale.y), R(t.localScale.z)),
                    ["wp"] = new JArray(R(t.position.x), R(t.position.y), R(t.position.z)),
                    ["c"] = components.Count > 0 ? components : null
                });
            }
            var renderers = new JArray();
            foreach (var renderer in actor.GetComponentsInChildren<Renderer>(true))
            {
                var skinned = renderer as SkinnedMeshRenderer;
                if (skinned == null && !(renderer is MeshRenderer)) continue;
                var entry = new JObject
                {
                    ["t"] = index[renderer.transform], ["kind"] = skinned != null ? "skinned" : "mesh", ["enabled"] = renderer.enabled, ["active"] = renderer.gameObject.activeInHierarchy,
                    ["bounds"] = new JArray(R(renderer.bounds.center.x), R(renderer.bounds.center.y), R(renderer.bounds.center.z), R(renderer.bounds.extents.x), R(renderer.bounds.extents.y), R(renderer.bounds.extents.z)),
                    ["materials"] = new JArray(renderer.sharedMaterials.Select(m => m != null ? m.name + " <" + (m.shader != null ? m.shader.name : "?") + ">" : null))
                };
                if (skinned != null)
                {
                    var mesh = skinned.sharedMesh;
                    entry["mesh"] = mesh != null ? mesh.name : null;
                    entry["readable"] = mesh != null && mesh.isReadable;
                    entry["offscreen"] = skinned.updateWhenOffscreen;
                    entry["rootBone"] = skinned.rootBone != null && index.TryGetValue(skinned.rootBone, out var rb) ? rb : -1;
                    var bones = skinned.bones;
                    entry["boneCount"] = bones.Length;
                    // the bones the mesh's vertices are bound to (where the mesh can be read); else all it lists
                    HashSet<int> used = null;
                    try
                    {
                        if (mesh != null && mesh.isReadable)
                        {
                            used = new HashSet<int>();
                            foreach (var weight in mesh.GetAllBoneWeights())
                                if (weight.weight > 0.01f) used.Add(weight.boneIndex);
                        }
                    }
                    catch (Exception) { used = null; }
                    var listed = new JArray();
                    for (var i = 0; i < bones.Length; i++)
                    {
                        if (used != null && !used.Contains(i)) continue;
                        listed.Add(bones[i] != null && index.TryGetValue(bones[i], out var b) ? b : -1);
                    }
                    entry["bones"] = listed;
                    entry["bound"] = used != null;
                }
                renderers.Add(entry);
            }
            var animators = new JArray();
            foreach (var animator in actor.GetComponentsInChildren<Animator>(true))
            {
                var entry = new JObject
                {
                    ["t"] = index[animator.transform], ["enabled"] = animator.enabled, ["active"] = animator.gameObject.activeInHierarchy, ["speed"] = animator.speed,
                    ["controller"] = animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : null,
                    ["rootMotion"] = animator.applyRootMotion, ["culling"] = animator.cullingMode.ToString(), ["update"] = animator.updateMode.ToString(), ["keep"] = animator.keepAnimatorStateOnDisable
                };
                try
                {
                    if (animator.runtimeAnimatorController != null)
                    {
                        var clips = new JArray();
                        foreach (var clip in animator.runtimeAnimatorController.animationClips)
                        {
                            if (clip == null) continue;
                            var events = new JArray();
                            foreach (var e in clip.events)
                                events.Add(new JObject { ["time"] = R(e.time), ["call"] = e.functionName, ["s"] = e.stringParameter, ["i"] = e.intParameter, ["f"] = R(e.floatParameter) });
                            clips.Add(new JObject { ["name"] = clip.name, ["length"] = R(clip.length), ["loop"] = clip.isLooping, ["events"] = events.Count > 0 ? events : null });
                        }
                        entry["clips"] = clips;
                    }
                    if (animator.isActiveAndEnabled && animator.runtimeAnimatorController != null)
                    {
                        entry["layers"] = animator.layerCount;
                        var parameters = new JArray();
                        foreach (var parameter in animator.parameters)
                        {
                            object value = parameter.type == AnimatorControllerParameterType.Float ? animator.GetFloat(parameter.nameHash)
                                : parameter.type == AnimatorControllerParameterType.Int ? animator.GetInteger(parameter.nameHash) : (object)animator.GetBool(parameter.nameHash);
                            parameters.Add(new JObject { ["name"] = parameter.name, ["type"] = parameter.type.ToString(), ["value"] = JToken.FromObject(value) });
                        }
                        entry["parameters"] = parameters;
                        entry["now"] = JToken.FromObject(animator.GetCurrentAnimatorClipInfo(0).Select(c => c.clip != null ? c.clip.name : "?").ToList());
                        entry["behaviours"] = JToken.FromObject(animator.GetBehaviours<StateMachineBehaviour>().Select(b => b.GetType().Name).GroupBy(n => n).ToDictionary(g => g.Key, g => g.Count()));
                    }
                }
                catch (Exception e) { entry["error"] = e.Message; }
                animators.Add(entry);
            }
            return new JObject
            {
                ["path"] = AgentBridge.PathOf(root), ["guid"] = actor.GetActorGuid(), ["cls"] = actor.ActorInstance?.ActorDataId, ["kit"] = actor.ActorInstance?.ActorWeaponKitId, ["skin"] = actor.ActorInstance?.ActorSkinId,
                ["scale"] = R(root.lossyScale.y), ["transforms"] = transforms, ["renderers"] = renderers, ["animators"] = animators, ["components"] = special
            };
        }

        private static float R(float value) => Mathf.Round(value * 10000f) / 10000f;
    }

    /// <summary>
    /// Dev bridge.
    /// corridor.weapons {} every weapon and prop of every hero in the dungeon view: shown or not, and why; what it
    ///   hangs on, the nearer hand and how far from it; "missing" counts the weapons that show in the model but
    ///   are not seen. {"all":true} the bodies too; {"scene":true} every hero model alive (a fight's, a camp's);
    ///   {"holders":true} how the component that lights each one stands; {"shown":true} leaves the switched-off out.
    /// corridor.weapons {"watch":true} keeps a log of every change of any weapon's state from now on (in any scene),
    ///   {"watch":false} ends it; {"log":true} the log ({"clear":true} empties it; {"since":n} from that entry).
    /// corridor.weapons {"guard":"state"} what the weapon guard has learnt and done ({"guard":false} switches it
    ///   off); {"hide":"dagger","hero":4} / {"show":"flask","hero":4} a fault made on purpose for it to put right.
    /// corridor.weapons {"relight":"refresh"|"renderers"|"apply"} a push at the component that lights what shows
    ///   unlit or undrawn (it does not help against a camera that does not look there: corridor.cameras).
    /// corridor.rig {"file":"D:/x/rig.json"} the whole of every model in the view (or {"scene":true}) to a file:
    ///   transforms, meshes and the bones they are bound to, animators with their clips and the clips' events,
    ///   every other component with what it points at.
    /// </summary>
    [EstateModule]
    internal static class CorridorWeaponsDev
    {
        private static void Register()
        {
            AgentBridge.Register("corridor.weapons", o =>
            {
                if (o["watch"] != null || (bool?)o["clear"] == true)
                {
                    var state = HeroParts.Watch((bool?)o["watch"], (bool?)o["all"] == true, (bool?)o["clear"] == true);
                    if ((bool?)o["log"] != true) return state;
                }
                if ((bool?)o["log"] == true)
                {
                    var since = (int?)o["since"] ?? 0;
                    return new { frame = Time.frameCount, count = HeroParts.Log.Count, changes = HeroParts.Log.Skip(since).ToList() };
                }
                // the guard (CorridorWeaponGuard.cs): {"guard":true|false} on or off, {"guard":"state"} what it knows and
                // has done; {"hide":"dagger","hero":4} a weapon mesh of the hero of that rank switched off on purpose
                // ({"show":"flask",...}: on), to see it put right
                if (o["hide"] != null || o["show"] != null)
                {
                    if (CorridorView.Instance == null) return "no corridor";
                    return CorridorView.Instance.BreakWeapon(((int?)o["hero"] ?? 1) - 1, (string)(o["hide"] ?? o["show"]), o["show"] != null);
                }
                if (o["guard"] != null)
                {
                    if (o["guard"].Type == JTokenType.Boolean) CorridorView.WeaponGuard = (bool)o["guard"];
                    return CorridorView.DescribeWeaponGuard();
                }
                var models = Models((bool?)o["scene"] == true);
                if (models == null) return "no corridor";
                var all = (bool?)o["all"] == true;
                var holders = (bool?)o["holders"] == true;
                var shownOnly = (bool?)o["shown"] == true;
                var heroes = new List<object>();
                var missing = 0;
                // {"relight":"refresh"|"renderers"|"apply"}: a try at what shows unlit or undrawn, through its holder
                var relight = (string)o["relight"];
                if (relight != null)
                {
                    var tried = new List<string>();
                    foreach (var actor in models)
                        foreach (var part in HeroParts.Look(actor, false))
                        {
                            if (!part.Active || !part.Enabled || part.Shown || part.Holder == null) continue;
                            if (relight == "refresh") part.Holder.ForceRefresh();
                            else if (relight == "renderers") part.Holder.RefreshRenderers();
                            else if (relight == "apply") part.Holder.Apply(part.Renderer, true, true);
                            else return "relight: refresh, renderers or apply";
                            tried.Add(part.Name);
                        }
                    return new { relight, tried };
                }
                foreach (var actor in models)
                {
                    var parts = HeroParts.Look(actor, holders);
                    var bad = parts.Count(p => p.Weapon && p.Active && p.Enabled && !p.Shown);
                    missing += bad;
                    heroes.Add(new
                    {
                        guid = actor.GetActorGuid(), cls = actor.ActorInstance?.ActorDataId, where = HeroParts.Where(actor), model = actor.name + "#" + actor.GetInstanceID(),
                        kit = actor.ActorInstance?.ActorWeaponKitId, skin = actor.ActorInstance?.ActorSkinId,
                        animators = HeroParts.Animators(actor), missing = bad,
                        parts = parts.Where(p => (all || p.Weapon) && (!shownOnly || (p.Active && p.Enabled))).Select(p => Describe(p, holders)).ToList()
                    });
                }
                return new { frame = Time.frameCount, mode = GameModeMgr.CurrentMode?.GetName(), missing, heroes };
            });

            AgentBridge.Register("corridor.rig", o =>
            {
                var file = (string)o["file"];
                if (string.IsNullOrEmpty(file)) return "give a file to write";
                var models = Models((bool?)o["scene"] == true);
                if (models == null) return "no corridor";
                var rigs = new JArray();
                foreach (var actor in models) rigs.Add(HeroParts.Rig(actor));
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllText(file, rigs.ToString(Newtonsoft.Json.Formatting.None));
                return new { file, models = rigs.Count };
            });
        }

        private static List<ActorBhv> Models(bool scene)
        {
            if (scene) return HeroParts.HeroModels();
            var view = CorridorView.Instance;
            if (view == null || !view.gameObject.activeInHierarchy) return null;
            return view.Actors.Where(a => a != null).ToList();
        }

        private static object Describe(HeroParts.Part p, bool holders)
        {
            return new
            {
                part = p.Name, kind = p.Kind, weapon = p.Weapon, state = p.Verdict,
                brightness = float.IsNaN(p.Brightness) ? (float?)null : p.Brightness, block = p.HasBlock, instanced = p.Instanced, visible = p.Visible,
                bone = p.Bone, underPelvis = p.UnderPelvis, underHand = p.UnderHand, hand = p.Hand, fromHand = float.IsNaN(p.FromHand) ? (float?)null : p.FromHand,
                path = p.Path,
                facts = !holders ? null : new
                {
                    layer = LayerMask.LayerToName(p.Renderer.gameObject.layer), offscreen = (p.Renderer as SkinnedMeshRenderer)?.updateWhenOffscreen,
                    middle = new[] { p.Renderer.bounds.center.x, p.Renderer.bounds.center.y, p.Renderer.bounds.center.z },
                    half = new[] { p.Renderer.bounds.extents.x, p.Renderer.bounds.extents.y, p.Renderer.bounds.extents.z },
                    material = p.Renderer.sharedMaterial != null ? p.Renderer.sharedMaterial.name : null, shadows = p.Renderer.shadowCastingMode.ToString()
                },
                holder = !holders ? null : p.Holder == null ? (object)"none" : new
                {
                    on = p.HolderPath, enabled = p.HolderOn, lastFrame = p.HolderFrame, firstFrame = p.HolderFirst, waits = p.HolderWaits, applied = p.HolderApplied,
                    renderers = p.HolderList, overrides = p.HolderOverrides, parent = p.HolderParent
                }
            };
        }
    }
}
