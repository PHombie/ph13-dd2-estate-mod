using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Assets.Code.Actor;
using Assets.Code.CommonLogic.Pooling;
using Assets.Code.CommonLogic.Presentation;
using Assets.Code.UI;
using DD2Estate.Core;
using DD2Estate.Dev;
using DD2Estate.Estate;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// Dev bridge, the dungeon screen measured against DD2's fight:
    ///
    ///   python tools/bridge.py run hud.bars
    ///       where the parts under every hero stand on the display, in pixels of the frame (x from the left, y
    ///       from the top): the hero, the health bar, the stress pips, the row of marks and the selection mark,
    ///       each as [left, right, middle, top, bottom]; "off" is how far a part's middle stands from the bar's.
    ///       In a corridor and at a camp it measures the mod's panels (RaidTrays), in a fight the game's own
    ///       (ActorInfoUiBhv): the two answers are compared number by number.
    ///   python tools/bridge.py run hud.skilllook [look=dd1|dd2] [arrow=#CFCFCF] [chosen=#AF925D] [shift=0] [reset=true]
    ///       the banner's row of skills: which look stands there, every skill's ground, picture and state with its
    ///       box on the display, the parts of the narrow "move"; with arguments the look is changed first.
    ///   python tools/bridge.py run fight.pick [place=2]
    ///       in a fight: DD2's own skill buttons from the left; place=n chooses one as a click does (to see how
    ///       the game itself shows a chosen skill).
    ///   python tools/bridge.py run dungeon.markers
    ///       every hallway tile that holds something, and the marker and words the map has for it.
    ///   python tools/bridge.py run corridor.shadows [offset=-30]
    ///       the shadow under every hero of the corridor as it is drawn now (its material and polygon offset);
    ///       offset=-30 puts DD2's own offset back (the shadow then lies over the boots), offset=0 the corridor's.
    ///   python tools/bridge.py run actors.renderers file=D:/x/renderers.txt
    ///       every actor model in the scene and what draws it: each renderer with its tag, layer, sorting, bounds,
    ///       materials (shader, queue, the depth properties) and what its property block overrides; the cameras.
    ///       What the shadow under a hero is, and what it is sorted against, is read from this.
    /// </summary>
    [EstateModule]
    internal static class DungeonLookDev
    {
        private static void Register()
        {
            AgentBridge.Register("hud.bars", o => Bars());
            AgentBridge.Register("corridor.shadows", o =>
            {
                var view = CorridorView.Instance;
                if (view == null || !view.gameObject.activeInHierarchy) return "no corridor";
                return view.DescribeShadows((float?)o["offset"]);
            });
            // The banner's row of skills: {} what stands in it and where; {"look":"dd1"} DD1's frames, {"look":"dd2"}
            // DD2's; {"arrow":"#CFCFCF","chosen":"#AF925D"} the colours of the arrows of "move"; {"shift":2} the narrow
            // place moved; {"reset":true} the copies and pictures made again.
            AgentBridge.Register("hud.skilllook", o =>
            {
                if (o["look"] != null) Dd2Skills.Enabled = (string)o["look"] != "dd1";
                if (o["arrow"] != null && ColorUtility.TryParseHtmlString((string)o["arrow"], out var arrow)) RaidMoveArt.ArrowColour = arrow;
                if (o["chosen"] != null && ColorUtility.TryParseHtmlString((string)o["chosen"], out var chosen)) RaidMoveArt.ArrowChosen = chosen;
                if (o["shift"] != null) RaidHeroPanel.MoveShift = (float)o["shift"];
                if ((bool?)o["reset"] == true)
                {
                    Dd2Skills.Reset();
                    RaidMoveArt.Forget();
                }
                // the row is built anew only when something was asked for: what the pointer rests on keeps its mark
                // (the request itself is two entries: the command and its name)
                if (o.Count > 2) RaidHeroPanel.RowVersion++;
                return DungeonHud.HeroPanel != null ? DungeonHud.HeroPanel.DescribeRow() : (object)"no dungeon HUD";
            });
            // In a fight: {"place":2} the skill in that place of DD2's own bar is chosen as a click chooses it; {} the bar's buttons.
            AgentBridge.Register("fight.pick", o =>
            {
                var buttons = new List<SelectableSkillButton>();
                foreach (var bar in UnityEngine.Object.FindObjectsOfType<SkillSelectionBhv>())
                    foreach (var button in bar.GetComponentsInChildren<SelectableSkillButton>(false)) buttons.Add(button);
                buttons.Sort((a, b) => Middle(a.transform).CompareTo(Middle(b.transform)));
                var place = (int?)o["place"] ?? 0;
                if (place > 0 && place <= buttons.Count) buttons[place - 1].OnClick(false);
                var list = new List<object>();
                foreach (var button in buttons) list.Add(new { name = button.name, skill = button.SkillId, valid = button.IsValid, selected = button.Selected, box = Box(button.transform) });
                return list;
            });
            // What the map says of every hallway tile that holds something: whether the party knows the tile, and the
            // marker and the words the map gives it (none for the tile where the party will have to eat).
            AgentBridge.Register("dungeon.markers", o =>
            {
                var x = DungeonRun.Current?.Exploration;
                if (x == null) return "no expedition";
                var tiles = new List<object>();
                for (var h = 0; h < x.Map.Hallways.Count; h++)
                {
                    var hall = x.Map.Hallways[h];
                    for (var i = 0; i < hall.Segments.Count; i++)
                    {
                        var content = hall.Segments[i].Content;
                        if (content == HallContent.Empty) continue;
                        tiles.Add(new
                        {
                            hallway = h, segment = i, content = content.ToString(), known = x.IsSegmentKnown(h, i), done = x.IsSegmentDone(h, i),
                            marker = Scouting.TileMarker(x, h, i), words = Scouting.TileHint(x, h, i)
                        });
                    }
                }
                return tiles;
            });
            AgentBridge.Register("actors.renderers", o =>
            {
                var file = (string)o["file"];
                if (string.IsNullOrEmpty(file)) return "give a file to write";
                var text = new StringBuilder();
                var count = Renderers(text, (string)o["filter"]);
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllText(file, text.ToString());
                return new { file, actors = count };
            });
        }

        private static float R(float value) => Mathf.Round(value * 10f) / 10f;

        private static float Middle(Transform part)
        {
            var box = Box(part);
            return box != null ? box[2] : 0f;
        }

        /// <summary>A part's box on the display: left, right, middle, top, bottom (y from the top); null when it is not there.</summary>
        public static float[] Box(Transform part)
        {
            var rect = part as RectTransform;
            if (rect == null || !rect.gameObject.activeInHierarchy) return null;
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            var canvas = rect.GetComponentInParent<Canvas>();
            var camera = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
            float left = float.MaxValue, right = float.MinValue, top = float.MaxValue, bottom = float.MinValue;
            foreach (var corner in corners)
            {
                var at = RectTransformUtility.WorldToScreenPoint(camera, corner);
                left = Mathf.Min(left, at.x);
                right = Mathf.Max(right, at.x);
                top = Mathf.Min(top, Screen.height - at.y);
                bottom = Mathf.Max(bottom, Screen.height - at.y);
            }
            return new[] { R(left), R(right), R((left + right) * 0.5f), R(top), R(bottom) };
        }

        /// <summary>The box around a part's children that are on and drawn (a row is as wide as what stands in it).</summary>
        public static float[] Around(Transform parent)
        {
            if (parent == null || !parent.gameObject.activeInHierarchy) return null;
            float[] all = null;
            for (var i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                var graphic = child.GetComponent<Graphic>();
                if (graphic != null && (!graphic.enabled || graphic.color.a <= 0.003f) && child.childCount == 0) continue;
                var box = Box(child);
                if (box == null) continue;
                if (all == null) all = new[] { box[0], box[1], 0f, box[3], box[4] };
                else
                {
                    all[0] = Mathf.Min(all[0], box[0]);
                    all[1] = Mathf.Max(all[1], box[1]);
                    all[3] = Mathf.Min(all[3], box[3]);
                    all[4] = Mathf.Max(all[4], box[4]);
                }
            }
            if (all != null) all[2] = R((all[0] + all[1]) * 0.5f);
            return all;
        }

        private static object Off(float[] part, float[] bar) => part != null && bar != null ? (object)R(part[2] - bar[2]) : null;

        private static T F<T>(object target, string name) where T : class
        {
            return target != null ? AccessTools.Field(target.GetType(), name)?.GetValue(target) as T : null;
        }

        private static object Bars()
        {
            var screen = new[] { Screen.width, Screen.height };
            var fight = UnityEngine.Object.FindObjectsOfType<ActorInfoUiBhv>();
            if (fight.Length > 0)
            {
                // the fight's own panels, the heroes' (team 0) from the left
                var camera = Camera.main;
                var rows = new List<KeyValuePair<float, object>>();
                var marks = new List<object>();
                foreach (var indicator in UnityEngine.Object.FindObjectsOfType<ActorSelectionIndicatorBhv>())
                    foreach (var field in new[] { "m_performerIndicators", "m_friendlyIndicators" })
                    {
                        if (!(AccessTools.Field(typeof(ActorSelectionIndicatorBhv), field)?.GetValue(indicator) is GameObject[] list)) continue;
                        foreach (var mark in list)
                            if (mark != null && mark.activeInHierarchy) marks.Add(new { kind = field, name = mark.name, box = Box(mark.transform) });
                    }
                foreach (var info in fight)
                {
                    if (info == null || !info.isActiveAndEnabled) continue;
                    var model = AccessTools.Field(typeof(ActorInfoUiBhv), "m_ActorTransform")?.GetValue(info) as Transform;
                    var offset = AccessTools.Field(typeof(ActorInfoUiBhv), "m_WorldspaceOffset")?.GetValue(info) is Vector3 v ? v : Vector3.zero;
                    var local = AccessTools.Field(typeof(ActorInfoUiBhv), "m_LocalYOffset")?.GetValue(info) is float y ? y : 0f;
                    var stress = F<StressBarBhv>(info, "m_StressBar");
                    var pool = F<GameObjectPoolBhv>(info, "m_TokenPoolBhv");
                    var health = F<StatusBarBhv>(info, "m_HealthBar");
                    var slider = F<Slider>(health, "m_Slider");
                    var bar = Box(F<RectTransform>(info, "m_HealthBarObj"));
                    var pips = Around(F<Transform>(stress, "m_stressPipContainer"));
                    var row = pool != null ? Box(pool.transform) : null;
                    var hero = model != null && camera != null ? R(camera.WorldToScreenPoint(model.position).x) : float.NaN;
                    var heroWithOffset = model != null && camera != null ? R(camera.WorldToScreenPoint(model.position + offset).x) : float.NaN;
                    var panel = Box(info.transform);
                    rows.Add(new KeyValuePair<float, object>(panel != null ? panel[2] : 0f, new
                    {
                        guid = info.ActorGuid, hero, heroWithOffset, worldOffset = new[] { offset.x, offset.y, offset.z }, localY = local,
                        panel, panelAt = new[] { ((RectTransform)info.transform).anchoredPosition.x, ((RectTransform)info.transform).anchoredPosition.y },
                        bar, slider = slider != null ? Box(slider.transform) : null,
                        pips, pipsOff = Off(pips, bar),
                        marksRow = row, marksRowOff = Off(row, bar), marks = pool != null ? Around(pool.transform) : null,
                        barOffHero = bar != null && !float.IsNaN(hero) ? (object)R(bar[2] - hero) : null
                    }));
                }
                rows.Sort((a, b) => a.Key.CompareTo(b.Key));
                var list2 = new List<object>();
                foreach (var row in rows) list2.Add(row.Value);
                return new { where = "fight", screen, panels = list2, indicators = marks };
            }
            var trays = DungeonHud.Trays;
            if (trays == null) return new { where = "nowhere", screen };
            return new { where = "dungeon", screen, scale = Dd2Hud.Scale, rise = Dd2Hud.BarsRise, panels = trays.Measure() };
        }

        // ---- what draws the actors -------------------------------------------------------------------------

        private static string N(float value) => value.ToString("0.####", CultureInfo.InvariantCulture);
        private static string V(Vector3 v) => N(v.x) + "," + N(v.y) + "," + N(v.z);

        private static readonly string[] Floats =
        {
            "_DepthOffset", "_ZWrite", "_ZTest", "_Cull", "_Surface", "_Blend", "_SrcBlend", "_DstBlend", "_QueueOffset", "_FlipShadow", "_Brightness", "_AlphaClip", "_Cutoff", "_Opacity", "_Alpha",
            "_StencilRef", "_StencilComp", "_ZOffset", "_Offset", "_OffsetFactor", "_OffsetUnits", "_SortingOffset", "_ShadowOpacity", "_ShadowAlpha"
        };

        private static string PathIn(Transform root, Transform part)
        {
            var names = new List<string>();
            for (var t = part; t != null && t != root; t = t.parent) names.Insert(0, t.name);
            return string.Join("/", names);
        }

        private static int Renderers(StringBuilder text, string filter)
        {
            text.Append("# screen ").Append(Screen.width).Append('x').Append(Screen.height).Append('\n');
            foreach (var camera in Camera.allCameras)
            {
                text.Append("camera ").Append(camera.name).Append(" depth ").Append(N(camera.depth)).Append(camera.orthographic ? " ortho " + N(camera.orthographicSize) : " fov " + N(camera.fieldOfView))
                    .Append(" at ").Append(V(camera.transform.position)).Append(" fwd ").Append(V(camera.transform.forward)).Append(" near ").Append(N(camera.nearClipPlane)).Append(" far ").Append(N(camera.farClipPlane))
                    .Append(" mask ").Append(camera.cullingMask).Append(" clear ").Append(camera.clearFlags).Append(" rect ").Append(camera.rect)
                    .Append(" sort ").Append(camera.transparencySortMode).Append(" axis ").Append(V(camera.transparencySortAxis)).Append(" opaqueSort ").Append(camera.opaqueSortMode);
                foreach (var extra in camera.GetComponents<MonoBehaviour>())
                {
                    if (extra == null || extra.GetType().Name != "UniversalAdditionalCameraData") continue;
                    var type = extra.GetType();
                    text.Append(" | urp type ").Append(type.GetProperty("renderType")?.GetValue(extra)).Append(" renderer ").Append(type.GetProperty("scriptableRenderer")?.GetValue(extra));
                }
                text.Append('\n');
            }
            var block = new MaterialPropertyBlock();
            var count = 0;
            foreach (var actor in UnityEngine.Object.FindObjectsOfType<ActorBhv>())
            {
                if (actor == null) continue;
                if (!string.IsNullOrEmpty(filter) && actor.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                count++;
                var root = actor.transform;
                text.Append("\n== ").Append(AgentBridge.PathOf(root)).Append(" guid ").Append(actor.GetActorGuid()).Append(root.gameObject.activeInHierarchy ? "" : " (OFF)")
                    .Append(" at ").Append(V(root.position)).Append(" scale ").Append(V(root.lossyScale)).Append(" rot ").Append(V(root.eulerAngles)).Append(" layer ").Append(LayerMask.LayerToName(root.gameObject.layer)).Append('\n');
                var camera = Camera.main;
                if (camera != null) text.Append("   on screen ").Append(V(camera.WorldToScreenPoint(root.position))).Append('\n');
                foreach (var holder in actor.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (holder == null) continue;
                    var name = holder.GetType().Name;
                    if (name == "MaterialPropertyBhv" || name == "SortingGroup" || name.IndexOf("Shadow", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Depth", StringComparison.OrdinalIgnoreCase) >= 0)
                        text.Append("   has ").Append(name).Append(" on ").Append(PathIn(root, holder.transform)).Append(holder.enabled ? "" : " (disabled)").Append('\n');
                }
                foreach (var group in actor.GetComponentsInChildren<UnityEngine.Rendering.SortingGroup>(true))
                    text.Append("   sorting group on ").Append(PathIn(root, group.transform)).Append(" layer ").Append(group.sortingLayerName).Append(" order ").Append(group.sortingOrder).Append('\n');
                foreach (var renderer in actor.GetComponentsInChildren<Renderer>(true))
                {
                    var t = renderer.transform;
                    text.Append("  ").Append(renderer.GetType().Name).Append(' ').Append(PathIn(root, t)).Append(" tag ").Append(t.tag).Append(renderer.gameObject.activeInHierarchy ? "" : " (OFF)").Append(renderer.enabled ? "" : " (disabled)")
                        .Append(" layer ").Append(LayerMask.LayerToName(renderer.gameObject.layer)).Append(" sort ").Append(renderer.sortingLayerName).Append('/').Append(renderer.sortingOrder).Append(" prio ").Append(renderer.rendererPriority)
                        .Append(" pos ").Append(V(t.position)).Append(" rot ").Append(V(t.eulerAngles)).Append(" scale ").Append(V(t.lossyScale))
                        .Append(" bounds ").Append(V(renderer.bounds.center)).Append(" +- ").Append(V(renderer.bounds.extents)).Append('\n');
                    var materials = renderer.sharedMaterials;
                    for (var i = 0; i < materials.Length; i++)
                    {
                        var material = materials[i];
                        if (material == null) continue;
                        text.Append("      mat ").Append(material.name).Append(" <").Append(material.shader != null ? material.shader.name : "?").Append("> queue ").Append(material.renderQueue);
                        foreach (var name in Floats)
                            if (material.HasProperty(name)) text.Append(' ').Append(name).Append('=').Append(N(material.GetFloat(name)));
                        if (material.HasProperty("_Color")) text.Append(" _Color=#").Append(ColorUtility.ToHtmlStringRGBA(material.GetColor("_Color")));
                        var tagType = material.GetTag("RenderType", false, "");
                        var tagQueue = material.GetTag("Queue", false, "");
                        text.Append(" tags ").Append(tagType).Append('/').Append(tagQueue);
                        if (material.shaderKeywords.Length > 0) text.Append(" keywords ").Append(string.Join(",", material.shaderKeywords));
                        if (renderer.HasPropertyBlock())
                        {
                            renderer.GetPropertyBlock(block);
                            var said = false;
                            foreach (var name in Floats)
                            {
                                if (!block.HasFloat(name)) continue;
                                text.Append(said ? " " : " | block ").Append(name).Append('=').Append(N(block.GetFloat(name)));
                                said = true;
                            }
                            renderer.GetPropertyBlock(block, i);
                            said = false;
                            foreach (var name in Floats)
                            {
                                if (!block.HasFloat(name)) continue;
                                text.Append(said ? " " : " | block[" + i + "] ").Append(name).Append('=').Append(N(block.GetFloat(name)));
                                said = true;
                            }
                        }
                        text.Append('\n');
                    }
                }
            }
            return count;
        }
    }
}
