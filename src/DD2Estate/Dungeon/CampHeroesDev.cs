using System;
using System.Collections.Generic;
using System.Reflection;
using Assets.Code.Actor;
using Assets.Code.UI.Widgets;
using DD2Estate.Dev;
using DD2Estate.Estate;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// Dev bridge: DD2's own rest stop as it is loaded behind the camp screen, read where the decompiled code
    /// cannot tell (what the prefab wires up): the hero slots of the inn (RestItemSlotBhv: the colour a hero
    /// under the pointer is lit with, the sound, what its roll-over events call), and the seated heroes' animators.
    ///
    ///   camp.inn        the slots and the heroes of the camp scene (a camp must be on screen)
    /// </summary>
    [EstateModule]
    internal static class CampHeroesDev
    {
        private static void Register()
        {
            CampHeroes.Enabled = Plugin.Settings.Bind("Camp", "HeroModels", true,
                "The heroes seated at a camp answer the pointer as at DD2's inn: the one under it is lit and looks up, a click selects them (or names the companion a camping skill waits for), a hero a skill is used on plays the inn's animation.").Value;
            AgentBridge.Register("camp.inn", o => Inn());
            // The seats as the pointer finds them; {"enabled":false} switches the feature off, {"width":373,"height":500}
            // the most a hero's rectangle measures (pixels of a 1080 high screen), {"share":0.96} how much of the
            // model's height takes the pointer, {"reach":4} what of a model counts for its box,
            // {"show":true} draws the rectangles, {"selected":false} lets the selected hero sit as the others,
            // {"pulse":1.1} how long a hero a skill was for stays lit.
            AgentBridge.Register("camp.heroes", o =>
            {
                if (o["enabled"] != null) CampHeroes.Enabled = (bool)o["enabled"];
                if (o["width"] != null) CampHeroes.SlotWidth = (float)o["width"];
                if (o["height"] != null) CampHeroes.SlotHeight = (float)o["height"];
                if (o["share"] != null) CampHeroes.HeightShare = (float)o["share"];
                if (o["reach"] != null) CampHeroes.Reach = (float)o["reach"];
                if (o["show"] != null) CampHeroes.ShowZones = (bool)o["show"];
                if (o["selected"] != null) CampHeroes.SelectedLooksUp = (bool)o["selected"];
                if (o["pulse"] != null) CampHeroes.UsePulse = (float)o["pulse"];
                return CampHeroes.Describe();
            });
            // An animator parameter of a seated hero, by hand: {"guid":3,"trigger":"inn_use_item"} or {"guid":3,"bool":"inn_rollover_item","value":true}.
            AgentBridge.Register("camp.anim", o =>
            {
                var guid = (uint)o["guid"];
                foreach (var actor in CampView.Seated)
                {
                    if (actor == null || actor.GetActorGuid() != guid) continue;
                    if (o["trigger"] != null) return "trigger " + actor.AttemptAnimatorTrigger((string)o["trigger"]);
                    if (o["bool"] != null) return "bool " + actor.AttemptAnimatorSetBool((string)o["bool"], (bool?)o["value"] ?? true);
                    if (o["state"] != null) return "state " + actor.SetAnimatorState((string)o["state"]);
                }
                return "no such hero is seated";
            });
            // The pointer on a seated hero ({"guid":3}; 0: on none), a click on one ({"guid":3,"click":true}),
            // without the mouse: what the screen does with the real pointer.
            AgentBridge.Register("camp.point", o =>
            {
                var guid = (uint?)o["guid"] ?? 0u;
                if ((bool?)o["click"] == true) CampHeroes.DevClick(guid);
                else CampHeroes.DevPoint(guid);
                return CampHeroes.Describe();
            });
        }

        private static object Field(object target, string name)
        {
            try { return AccessTools.Field(target.GetType(), name)?.GetValue(target); }
            catch (Exception) { return null; }
        }

        private static List<object> Calls(UnityEventBase e)
        {
            var calls = new List<object>();
            if (e == null) return calls;
            for (var i = 0; i < e.GetPersistentEventCount(); i++)
            {
                var target = e.GetPersistentTarget(i);
                calls.Add(new { target = target != null ? target.GetType().Name + " '" + target.name + "'" : null, method = e.GetPersistentMethodName(i) });
            }
            return calls;
        }

        private static string Path(Transform t)
        {
            if (t == null) return null;
            var path = t.name;
            for (var p = t.parent; p != null && path.Length < 200; p = p.parent) path = p.name + "/" + path;
            return path;
        }

        private static object Inn()
        {
            var scene = SceneManager.GetSceneByName(CampView.SceneName);
            if (!scene.IsValid() || !scene.isLoaded) return "the camp scene is not loaded";
            var slots = new List<object>();
            var heroes = new List<object>();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var slot in root.GetComponentsInChildren<RestItemSlotBhv>(true))
                {
                    var colour = Field(slot, "m_actorHoverColor") is Color c ? new[] { c.r, c.g, c.b, c.a } : null;
                    var hover = Field(slot, "m_onHoverEventRef");
                    var rect = slot.transform as RectTransform;
                    var canvas = slot.GetComponentInParent<Canvas>();
                    slots.Add(new
                    {
                        path = Path(slot.transform), active = slot.gameObject.activeInHierarchy, colour,
                        hoverEvent = hover != null ? hover.ToString() : null,
                        hoverGuid = hover != null ? Convert.ToString(hover.GetType().GetField("Guid")?.GetValue(hover)) : null,
                        rollover = Calls(Field(slot, "m_RolloverEvent") as UnityEventBase),
                        rollout = Calls(Field(slot, "m_RolloutEvent") as UnityEventBase),
                        indicatorHover = Path((Field(slot, "m_indicatorHover") as GameObject)?.transform),
                        imageHighlight = Path((Field(slot, "m_imageHighlight") as GameObject)?.transform),
                        size = rect != null ? new[] { rect.rect.width, rect.rect.height } : null,
                        position = new[] { slot.transform.position.x, slot.transform.position.y, slot.transform.position.z },
                        canvas = canvas != null ? canvas.name + " " + canvas.renderMode : null
                    });
                }
                foreach (var actor in root.GetComponentsInChildren<ActorBhv>(true))
                {
                    var animator = actor.GetCurrentAnimator();
                    var parameters = new List<string>();
                    string state = null, controller = null;
                    var has = new List<string>();
                    if (animator != null)
                    {
                        foreach (var p in animator.parameters) parameters.Add(p.name + ":" + p.type);
                        controller = animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : null;
                        var info = animator.GetCurrentAnimatorStateInfo(0);
                        foreach (var name in new[] { "inn_idle", "inn_rollover_item", "inn_use_item", "inn_select", "inn_hover", "idle", "combat_idle", "inn_idle_rollover", "inn_rollover", "inn_use" })
                        {
                            if (animator.HasState(0, Animator.StringToHash(name))) has.Add(name);
                            if (info.IsName(name)) state = name;
                        }
                        var clips = animator.GetCurrentAnimatorClipInfo(0);
                        if (clips.Length > 0 && clips[0].clip != null) state = (state ?? "?") + " (clip " + clips[0].clip.name + ")";
                    }
                    var colliders = actor.GetComponentsInChildren<Collider>(true).Length;
                    var bounds = new Bounds(actor.transform.position, Vector3.zero);
                    var renderers = 0;
                    foreach (var renderer in actor.GetComponentsInChildren<Renderer>())
                    {
                        if (!renderer.enabled) continue;
                        if (renderers++ == 0) bounds = renderer.bounds;
                        else bounds.Encapsulate(renderer.bounds);
                    }
                    heroes.Add(new
                    {
                        guid = actor.GetActorGuid(), path = Path(actor.transform), controller, state, parameters, has, colliders,
                        blocks = actor.MaterialPropertyBlocks != null ? actor.MaterialPropertyBlocks.Count : 0,
                        renderers, centre = new[] { bounds.center.x, bounds.center.y, bounds.center.z }, size = new[] { bounds.size.x, bounds.size.y, bounds.size.z }
                    });
                }
            }
            return new { slots, heroes };
        }
    }
}
