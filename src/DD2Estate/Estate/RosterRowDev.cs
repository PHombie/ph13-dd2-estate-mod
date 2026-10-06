using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Assets.Code.Actor;
using Assets.Code.Library;
using Assets.Code.Utils;
using DD2Estate.Dev;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DD2Estate.Estate
{
    /// <summary>
    /// Test commands for what stands on a roster row and for looking at the screen at another window size
    /// (python tools/bridge.py run ui.window width=1706 height=960).
    /// </summary>
    [EstateModule]
    internal static class RosterRowDev
    {
        private static void Register()
        {
            // The game's window: {"width":1706,"height":960} makes it a window of that size (the canvas of
            // everything the mod draws is scaled to it); without numbers it only tells. Put it back afterwards.
            AgentBridge.Register("ui.window", o =>
            {
                if (o["width"] != null && o["height"] != null)
                    Screen.SetResolution((int)o["width"], (int)o["height"], o["mode"] != null ? (FullScreenMode)(int)o["mode"] : FullScreenMode.Windowed);
                return new { width = Screen.width, height = Screen.height, mode = Screen.fullScreenMode.ToString(), modeNumber = (int)Screen.fullScreenMode };
            });

            // The roster's rows as they stand: where each row's picture begins and ends and whether it meets the row
            // above, and the badge of the hero's path with what the pointer is told on it.
            AgentBridge.Register("roster.rows", o => RosterPanel.DescribeRows());

            // How large the rows' portraits are drawn in their slots; {"size":140} sets it for the rows that are up.
            AgentBridge.Register("roster.portrait", o => new { size = RosterPanel.PortraitsAt((float?)o["size"]) });

            // Every class's portrait in a slot, as a strip at the screen's left: {"classes":"highwayman,grave_robber"};
            // {"centre":false} shows them as DD2 painted them, unmoved; no classes takes the strip away.
            // {"shifts":"10:0,,18:-6"} moves a cell by its own numbers (right and up; an empty one keeps its
            // class's), {"seal":true} lays the party's seal on every cell.
            AgentBridge.Register("roster.faces", o =>
            {
                if (o["centre"] != null) RosterPanel.CentreFaces = (bool)o["centre"];
                var named = (string)o["classes"];
                List<Vector2?> shifts = null;
                if (o["shifts"] != null)
                {
                    shifts = new List<Vector2?>();
                    foreach (var one in ((string)o["shifts"]).Split(','))
                    {
                        var xy = one.Split(':');
                        if (xy.Length == 2 && float.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) && float.TryParse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                            shifts.Add(new Vector2(x, y));
                        else shifts.Add(null);
                    }
                }
                return RosterPanel.Faces(string.IsNullOrEmpty(named) ? null : named.Split(','), shifts, (bool?)o["seal"] ?? false);
            });

            // A class's nudge in the rows: {"cls":"highwayman","x":18,"y":-4} sets it for this run of the game (the
            // name is the ending of the portrait's sprite: graverobber, manatarms ...); without numbers it tells all.
            AgentBridge.Register("roster.nudge", o => RosterPanel.Nudge((string)o["cls"], (float?)o["x"], (float?)o["y"]));

            // A hero's path and the paths of the class; {"guid":2,"path":"..."} puts the hero on another (a test's
            // shortcut past the stage coach: the estate's rule of one hero per class and path is not asked).
            AgentBridge.Register("roster.path", o =>
            {
                var actor = SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance.GetLibraryElement((uint?)o["guid"] ?? 0u);
                if (actor == null) return "no such hero";
                var set = o["path"] != null ? (bool?)HeroPaths.Set(actor, (string)o["path"]) : null;
                return new { guid = actor.ActorGuid, cls = actor.ActorDataId, path = HeroPaths.Of(actor), name = HeroPaths.Name(actor), set, paths = HeroPaths.Of(actor.ActorDataId) };
            });

            // Where DD2 itself draws a path's seal right now: every picture in memory that shows one of the seals
            // (or, {"like":"ribbon"}, whose own or whose sprite's name has that word in it), with what stands
            // beside it and one step up. For finding the art of DD2's badge in its own screens.
            AgentBridge.Register("roster.seals", o => Seals((string)o["like"], (int?)o["limit"] ?? 12));
        }

        private static object Seals(string like, int limit)
        {
            var seals = new HashSet<Sprite>();
            var paths = SingletonMonoBehaviour<Library<string, ActorDataPath>>.Instance;
            var known = new List<object>();
            for (var i = 0; paths != null && i < paths.GetNumberOfLibraryElements(); i++)
            {
                var path = paths.GetLibraryElementAtIndex(i);
                if (path == null) continue;
                Sprite seal = null;
                try { seal = Singleton<ResourceDatabaseActorPaths>.Instance.GetResource(path.Id, isErrorValid: false)?.m_SealIcon; }
                catch (Exception) { /* a path without a resource has no seal */ }
                if (seal != null) seals.Add(seal);
                known.Add(new { path = path.Id, seal = seal != null ? seal.name : null, size = seal != null ? new[] { seal.rect.width, seal.rect.height } : null });
            }

            var found = new List<object>();
            foreach (var image in Resources.FindObjectsOfTypeAll<Image>())
            {
                if (image == null || image.sprite == null) continue;
                var hit = like == null
                    ? seals.Contains(image.sprite)
                    : image.name.IndexOf(like, StringComparison.OrdinalIgnoreCase) >= 0 || image.sprite.name.IndexOf(like, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!hit) continue;
                if (found.Count >= limit) break;
                var parent = image.transform.parent;
                found.Add(new
                {
                    at = Path(image.transform), inScene = image.gameObject.scene.IsValid(), active = image.gameObject.activeInHierarchy, self = Picture(image.transform),
                    beside = parent != null ? Children(parent) : null,
                    above = parent != null && parent.parent != null ? Children(parent.parent) : null
                });
            }
            return new { paths = known, found };
        }

        private static List<object> Children(Transform parent)
        {
            var list = new List<object>();
            for (var i = 0; i < parent.childCount && i < 24; i++) list.Add(Picture(parent.GetChild(i)));
            return list;
        }

        private static object Picture(Transform transform)
        {
            var rect = transform as RectTransform;
            var image = transform.GetComponent<Image>();
            var sprite = image != null ? image.sprite : null;
            return new
            {
                name = transform.name, active = transform.gameObject.activeSelf,
                size = rect != null ? new[] { rect.rect.width, rect.rect.height } : null,
                at = rect != null ? new[] { rect.anchoredPosition.x, rect.anchoredPosition.y } : null,
                anchors = rect != null ? new[] { rect.anchorMin.x, rect.anchorMin.y, rect.anchorMax.x, rect.anchorMax.y } : null,
                pivot = rect != null ? new[] { rect.pivot.x, rect.pivot.y } : null,
                scale = new[] { transform.localScale.x, transform.localScale.y },
                sprite = sprite != null ? sprite.name : null,
                spriteSize = sprite != null ? new[] { sprite.rect.width, sprite.rect.height } : null,
                texture = sprite != null && sprite.texture != null ? sprite.texture.name : null,
                colour = image != null ? "#" + ColorUtility.ToHtmlStringRGBA(image.color) : null,
                material = image != null && image.material != null ? image.material.name : null,
                type = image != null ? image.type.ToString() : null,
                components = string.Join(",", transform.GetComponents<Component>().Where(c => c != null && !(c is Transform) && !(c is CanvasRenderer)).Select(c => c.GetType().Name))
            };
        }

        private static string Path(Transform transform)
        {
            var names = new List<string>();
            for (var at = transform; at != null && names.Count < 12; at = at.parent) names.Insert(0, at.name);
            return string.Join("/", names);
        }
    }
}
