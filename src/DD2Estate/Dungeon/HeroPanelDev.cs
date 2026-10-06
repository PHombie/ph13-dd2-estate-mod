using System;
using System.Collections.Generic;
using Assets.Code.Actor;
using Assets.Code.Combat;
using Assets.Code.CommonLogic.Presentation;
using Assets.Code.Library;
using Assets.Code.Skill;
using Assets.Code.Source;
using Assets.Code.Utils;
using DD2Estate.Dev;
using DD2Estate.Estate;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// Dev bridge, the selected hero's panel:
    /// hud.stats {} the four lines as they stand on the panel and DD2's numbers behind them, source by source
    /// ({"guid":n}: the numbers of another hero);
    /// hud.dd2move {} what DD2 itself shows a move with, read from the running game: the hero's move skill and its
    /// icon, the marks of a fight's targets (ActorSelectionIndicatorBhv), the curves a fight takes a hero to
    /// another rank along (CombatActorBhv);
    /// hud.movemarks {} the marks of "move" and whether DD2's pictures for them are in memory, {"look":1} DD2's;
    /// dev.window {"width":1706,"height":960} the game's window at another size, {"restore":true} as it was.
    /// </summary>
    [EstateModule]
    internal static class HeroPanelDev
    {
        private static void Register()
        {
            AgentBridge.Register("hud.stats", o =>
            {
                var guid = o["guid"] != null ? (uint)o["guid"] : DungeonHud.Selected;
                var actor = guid != 0u ? SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance.GetLibraryElement(guid) : null;
                if (actor == null) return "no hero";
                return new
                {
                    panel = DungeonHud.HeroPanel?.DescribeStats(),
                    hero = actor.ActorName,
                    cls = actor.ActorDataId,
                    damageFactor = RaidHeroPanel.DamageFactor(actor),
                    damageAdds = BySource(actor.GetAddStatValuesBySource(ActorStatType.HEALTH_DAMAGE_DEALT_PERCENT, false)),
                    damageFactors = BySource(actor.GetMultiplyStatValuesBySource(ActorStatType.HEALTH_DAMAGE_DEALT_MULT_PERCENT, false)),
                    damageClamped = actor.GetClampedStatValue(ActorStatType.HEALTH_DAMAGE_DEALT_PERCENT),
                    crit = RaidHeroPanel.CritBonus(actor),
                    critAdds = BySource(actor.GetAddStatValuesBySource(ActorStatType.CRIT_CHANCE, false)),
                    healthShare = RaidHeroPanel.HealthShare(actor),
                    healthShares = BySource(actor.GetMultiplyStatValuesBySource(ActorStatType.HEALTH_MAX, false)),
                    healthAdds = BySource(actor.GetAddStatValuesBySource(ActorStatType.HEALTH_MAX, false)),
                    healthMax = actor.GetClampedStatValue(ActorStatType.HEALTH_MAX),
                    speed = actor.GetClampedStatValue(ActorStatType.SPEED),
                    speedAdds = BySource(actor.GetAddStatValuesBySource(ActorStatType.SPEED, false)),
                    weapon = Blacksmith.Level(guid, Blacksmith.Gear.Weapon),
                    armour = Blacksmith.Level(guid, Blacksmith.Gear.Armour)
                };
            });
            AgentBridge.Register("hud.dd2move", o => Dd2Move());
            // The marks of the places "move" can take the hero to: {} what stands there and whether the game has
            // given DD2's own pictures of a target's mark; {"look":0} DD1's mark instead, {"look":1} DD2's again
            // (RaidMoveMarks.Look).
            AgentBridge.Register("hud.movemarks", o =>
            {
                if (o["look"] != null) RaidMoveMarks.Look = (int)o["look"];
                return new { look = RaidMoveMarks.Look, dd2Loaded = RaidMoveMarks.Dd2PicturesLoaded, dd2Pictures = RaidMoveMarks.Dd2Status, hud = DungeonHud.DevSkill(0) };
            });
            // The game's window at another size, to photograph a screen as a smaller display shows it:
            // {"width":1706,"height":960}; {"restore":true} the size and mode it had before the first change (do
            // this before the game is closed: Unity remembers the last size for the player's next start).
            AgentBridge.Register("dev.window", o =>
            {
                if (o["width"] != null && o["height"] != null)
                {
                    if (_windowBefore == null) _windowBefore = new[] { Screen.width, Screen.height, (int)Screen.fullScreenMode };
                    Screen.SetResolution((int)o["width"], (int)o["height"], FullScreenMode.Windowed);
                }
                else if ((bool?)o["restore"] == true && _windowBefore != null)
                {
                    Screen.SetResolution(_windowBefore[0], _windowBefore[1], (FullScreenMode)_windowBefore[2]);
                    _windowBefore = null;
                }
                return new { width = Screen.width, height = Screen.height, mode = Screen.fullScreenMode.ToString(), changed = _windowBefore != null };
            });
        }

        private static int[] _windowBefore;

        private static Dictionary<string, float> BySource(IReadOnlyDictionary<SourceType, float> values)
        {
            var named = new Dictionary<string, float>();
            foreach (var pair in values) named[pair.Key != null ? pair.Key.GetName() : "none"] = pair.Value;
            return named;
        }

        private static object Dd2Move()
        {
            var skills = new List<object>();
            try
            {
                var actor = DungeonHud.Selected != 0u ? SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance.GetLibraryElement(DungeonHud.Selected) : null;
                if (actor != null)
                    foreach (var id in actor.GetEquippedCombatSkillIds(null, false, false, true, true, false, false))
                    {
                        var skill = SingletonMonoBehaviour<Library<string, ActorDataSkill>>.Instance.GetLibraryElement(id);
                        if (skill == null || !(skill.IsMoveSkill || skill.IsPassSkill)) continue;
                        var icon = Guild.Icon(id);
                        skills.Add(new { id, move = skill.IsMoveSkill, pass = skill.IsPassSkill, icon = icon != null ? icon.name : null, size = icon != null ? new[] { icon.rect.width, icon.rect.height } : null });
                    }
            }
            catch (Exception e) { skills.Add("skills: " + e.Message); }

            var marks = new List<object>();
            try
            {
                foreach (var spawner in Resources.FindObjectsOfTypeAll<ActorIndicatorSpawnerBhv>())
                {
                    var prefab = AccessTools.Field(typeof(ActorIndicatorSpawnerBhv), "m_ActorSelectionIndicatorPrefab")?.GetValue(spawner) as GameObject;
                    var indicator = prefab != null ? prefab.GetComponent<ActorSelectionIndicatorBhv>() : null;
                    marks.Add(new
                    {
                        spawner = spawner.name, scene = spawner.gameObject.scene.name, prefab = prefab != null ? prefab.name : null,
                        performer = Indicators(indicator, "m_performerIndicators"), friendly = Indicators(indicator, "m_friendlyIndicators"), enemy = Indicators(indicator, "m_enemyIndicators")
                    });
                }
            }
            catch (Exception e) { marks.Add("marks: " + e.Message); }

            var curves = new List<object>();
            try
            {
                foreach (var actor in Resources.FindObjectsOfTypeAll<CombatActorBhv>())
                {
                    curves.Add(new { actor = actor.name, forward = Curves(actor, "m_ChangeRankForwardCurves"), back = Curves(actor, "m_ChangeRankBackCurves") });
                    if (curves.Count >= 2) break;
                }
            }
            catch (Exception e) { curves.Add("curves: " + e.Message); }
            return new { skills, marks, curves };
        }

        private static object Indicators(ActorSelectionIndicatorBhv indicator, string field)
        {
            if (indicator == null) return null;
            var list = new List<object>();
            if (AccessTools.Field(typeof(ActorSelectionIndicatorBhv), field)?.GetValue(indicator) is GameObject[] objects)
                foreach (var one in objects) list.Add(one != null ? Tree(one.transform, 0) : null);
            return list;
        }

        private static object Tree(Transform node, int depth)
        {
            var rect = node as RectTransform;
            var image = node.GetComponent<Image>();
            var children = new List<object>();
            if (depth < 4)
                foreach (Transform child in node) children.Add(Tree(child, depth + 1));
            var behaviours = new List<string>();
            foreach (var behaviour in node.GetComponents<Component>())
                if (behaviour != null && !(behaviour is Transform) && !(behaviour is CanvasRenderer)) behaviours.Add(behaviour.GetType().Name);
            return new
            {
                name = node.name, active = node.gameObject.activeSelf,
                size = rect != null ? new[] { rect.sizeDelta.x, rect.sizeDelta.y } : null,
                at = rect != null ? new[] { rect.anchoredPosition.x, rect.anchoredPosition.y } : null,
                pivot = rect != null ? new[] { rect.pivot.x, rect.pivot.y } : null,
                scale = new[] { node.localScale.x, node.localScale.y },
                sprite = image != null && image.sprite != null ? image.sprite.name : null,
                spriteSize = image != null && image.sprite != null ? new[] { image.sprite.rect.width, image.sprite.rect.height } : null,
                colour = image != null ? "#" + ColorUtility.ToHtmlStringRGBA(image.color) : null,
                material = image != null && image.material != null ? image.material.name : null,
                behaviours, children
            };
        }

        private static object Curves(CombatActorBhv actor, string field)
        {
            var list = new List<object>();
            if (AccessTools.Field(typeof(CombatActorBhv), field)?.GetValue(actor) is List<AnimationCurve> curves)
                foreach (var curve in curves)
                {
                    var keys = new List<float[]>();
                    foreach (var key in curve.keys) keys.Add(new[] { key.time, key.value, key.inTangent, key.outTangent });
                    list.Add(keys);
                }
            return list;
        }
    }
}
