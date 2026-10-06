using System;
using System.Collections.Generic;
using Assets.Code.Actor;
using UnityEngine;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// A guard over the heroes' weapons in the corridor, on top of the real fixes (CorridorCulling.cs: weapons
    /// at the view's sides were not drawn; CorridorGait.cs: weapons beside the pelvis left the hand).
    ///
    /// DD2's clips switch a model's weapon meshes on and off (a flask for the dagger, a pickaxe, a pistol), and
    /// each clip leaves the model as the next one expects it. The corridor plays only a few of them (the
    /// fighting idle; the step of a change of rank and its recovery) and none of those was seen to leave a
    /// weapon wrong (every class, 2026-10-06). The guard is for what was not seen: another skin's or weapon
    /// kit's clips, a step cut short by the next order.
    ///
    /// What is right is learnt from the models themselves: a model the corridor has made and sent no trigger
    /// to stands in its idle as the game made it, and which of its weapon meshes show there is kept by class,
    /// skin, weapon kit and idle clip. A model that has stepped and stands in the same idle again is compared
    /// with that: a weapon mesh that should show is switched on again, one that should not is switched off, and
    /// the log says so. A mesh the idle itself switches (seen differing between untouched models, or switched
    /// back by the animator after the guard) is left to the animation. A hero who has not come back to the
    /// idle some seconds after a step has ended is cross-faded into it.
    /// </summary>
    internal partial class CorridorView
    {
        /// <summary>The guard is on.</summary>
        public static bool WeaponGuard = true;
        /// <summary>Seconds after a change of rank has ended before a hero still out of the idle is put back into it (the longest recovery clip lasts 2.2).</summary>
        public static float GuardIdleAfter = 5f;

        private const string IdleState = "idle_neutral";

        private class WeaponWatch
        {
            public bool Stirred, Forced;
            public float StirredAt, Since;
            public string Clip;
            public List<Renderer> Weapons;
            public readonly Dictionary<string, int> Wrong = new Dictionary<string, int>();
            public readonly HashSet<string> Righted = new HashSet<string>();
        }

        private class IdleLook
        {
            public readonly Dictionary<string, bool> Shows = new Dictionary<string, bool>();
            public readonly HashSet<string> Varies = new HashSet<string>();
        }

        private readonly Dictionary<ActorBhv, WeaponWatch> _weaponWatch = new Dictionary<ActorBhv, WeaponWatch>();
        // by class, skin, weapon kit and idle clip: learnt from untouched models, kept for the session
        private static readonly Dictionary<string, IdleLook> IdleLooks = new Dictionary<string, IdleLook>();
        private static readonly List<string> GuardNotes = new List<string>();
        private float _nextWeaponGuard;

        /// <summary>A trigger of the mod's goes to this model's animator (a change of rank): from now on it is compared, no longer learnt from.</summary>
        private void StirWeaponGuard(ActorBhv actor)
        {
            if (actor == null) return;
            if (!_weaponWatch.TryGetValue(actor, out var watch)) _weaponWatch[actor] = watch = new WeaponWatch();
            watch.Stirred = true;
            watch.StirredAt = Time.time;
            watch.Clip = null;
        }

        // Four times a second, from Update.
        private void GuardWeapons()
        {
            if (!WeaponGuard || Time.unscaledTime < _nextWeaponGuard) return;
            _nextWeaponGuard = Time.unscaledTime + 0.25f;
            try
            {
                if (_weaponWatch.Count > _actors.Count)
                {
                    var gone = new List<ActorBhv>();
                    foreach (var actor in _weaponWatch.Keys)
                        if (actor == null || !_actors.Contains(actor)) gone.Add(actor);
                    foreach (var actor in gone) _weaponWatch.Remove(actor);
                }
                for (var i = 0; i < _actors.Count; i++)
                    if (_actors[i] != null) GuardWeapons(_actors[i], _actorRank[i]);
            }
            catch (Exception e)
            {
                // a guard: never let it stop the view
                Plugin.Log.LogWarning("Corridor: the weapon guard has stopped: " + e.Message);
                WeaponGuard = false;
            }
        }

        private void GuardWeapons(ActorBhv actor, int rank)
        {
            if (!_weaponWatch.TryGetValue(actor, out var watch)) _weaponWatch[actor] = watch = new WeaponWatch();
            var animator = actor.GetCurrentAnimator();
            if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null || animator.layerCount == 0) return;
            if (_rankMoves.ContainsKey(rank))
            {
                watch.Stirred = true;
                watch.StirredAt = Time.time;
                watch.Clip = null;
                return;
            }
            if (!animator.GetCurrentAnimatorStateInfo(0).IsName(IdleState) || animator.IsInTransition(0))
            {
                watch.Clip = null;
                if (!watch.Stirred || watch.Forced || Time.time - watch.StirredAt < GuardIdleAfter || !animator.HasState(0, Animator.StringToHash(IdleState))) return;
                watch.Forced = true;
                animator.CrossFadeInFixedTime(IdleState, 0.25f);
                Note(actor, "had not come back to the idle " + GuardIdleAfter.ToString("0.#") + " s after a step: put back into it");
                return;
            }
            watch.Forced = false;
            var clips = animator.GetCurrentAnimatorClipInfo(0);
            var clip = clips.Length > 0 && clips[0].clip != null ? clips[0].clip.name : "";
            if (clip != watch.Clip)
            {
                watch.Clip = clip;
                watch.Since = Time.time;
                watch.Wrong.Clear();
                return;
            }
            if (Time.time - watch.Since < 0.5f) return;

            if (watch.Weapons == null || watch.Weapons.Count == 0 || watch.Weapons[0] == null)
            {
                watch.Weapons = new List<Renderer>();
                foreach (var renderer in actor.GetComponentsInChildren<Renderer>(true))
                    if ((renderer is SkinnedMeshRenderer || renderer is MeshRenderer) && HeroParts.IsWeapon(renderer.name)) watch.Weapons.Add(renderer);
                if (watch.Weapons.Count == 0) return;
            }
            var hero = actor.ActorInstance;
            var key = (hero != null ? hero.ActorDataId + "|" + hero.ActorSkinId + "|" + hero.ActorWeaponKitId : actor.name) + "|" + clip;
            if (!IdleLooks.TryGetValue(key, out var look))
            {
                if (watch.Stirred) return;      // nobody has stood untouched in this idle yet
                IdleLooks[key] = look = new IdleLook();
            }
            foreach (var renderer in watch.Weapons)
            {
                if (renderer == null) continue;
                var path = HeroParts.PathIn(actor.transform, renderer.transform);
                var shows = renderer.gameObject.activeInHierarchy && renderer.enabled;
                if (!watch.Stirred)
                {
                    // as the game made it: this is what the idle looks like (what differs from model to model is the idle's own doing)
                    if (!look.Shows.TryGetValue(path, out var known)) look.Shows[path] = shows;
                    else if (known != shows) look.Varies.Add(path);
                    continue;
                }
                if (look.Varies.Contains(path) || !look.Shows.TryGetValue(path, out var should)) continue;
                if (should == shows)
                {
                    watch.Wrong.Remove(path);
                    continue;
                }
                if (watch.Righted.Contains(path))
                {
                    // put right once, and wrong again: the animation switches it itself
                    look.Varies.Add(path);
                    Note(actor, renderer.name + " is switched by the idle itself: left to it");
                    continue;
                }
                watch.Wrong.TryGetValue(path, out var times);
                watch.Wrong[path] = ++times;
                if (times < 2) continue;
                if (should)
                {
                    for (var t = renderer.transform; t != null && t != actor.transform; t = t.parent)
                        if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
                    renderer.enabled = true;
                }
                else renderer.gameObject.SetActive(false);
                watch.Righted.Add(path);
                watch.Wrong.Remove(path);
                Note(actor, renderer.name + (should ? " was left switched off: switched on again" : " was left showing: switched off again") + " (idle " + clip + ")");
            }
        }

        private static void Note(ActorBhv actor, string what)
        {
            var hero = actor.ActorInstance;
            var line = (hero != null ? hero.ActorName + " the " + hero.ActorDataId : actor.name) + ": " + what;
            Plugin.Log.LogInfo("Corridor: weapon guard: " + line);
            if (GuardNotes.Count >= 100) GuardNotes.RemoveAt(0);
            GuardNotes.Add(Time.frameCount + " " + line);
        }

        /// <summary>Dev bridge: what the guard knows and what it has done.</summary>
        internal static object DescribeWeaponGuard()
        {
            var looks = new List<object>();
            foreach (var pair in IdleLooks)
            {
                var shown = new List<string>();
                foreach (var part in pair.Value.Shows)
                    if (part.Value) shown.Add(part.Key.Substring(part.Key.LastIndexOf('/') + 1));
                looks.Add(new { idle = pair.Key, shows = shown, parts = pair.Value.Shows.Count, varies = new List<string>(pair.Value.Varies) });
            }
            var heroes = new List<object>();
            if (Instance != null)
                foreach (var pair in Instance._weaponWatch)
                    if (pair.Key != null) heroes.Add(new { hero = pair.Key.GetActorGuid(), stirred = pair.Value.Stirred, clip = pair.Value.Clip, weapons = pair.Value.Weapons != null ? pair.Value.Weapons.Count : -1, righted = new List<string>(pair.Value.Righted) });
            return new { on = WeaponGuard, idleAfter = GuardIdleAfter, looks, heroes, notes = new List<string>(GuardNotes) };
        }

        /// <summary>
        /// Dev bridge: a fault made on purpose, to see it put right: the weapon mesh whose name holds
        /// <paramref name="part"/> on the hero of a rank is switched off (or on), as a clip might leave it, and
        /// the model counts as one that has stepped.
        /// </summary>
        internal string BreakWeapon(int rank, string part, bool show)
        {
            for (var i = 0; i < _actors.Count; i++)
            {
                if (_actors[i] == null || _actorRank[i] != rank) continue;
                foreach (var renderer in _actors[i].GetComponentsInChildren<Renderer>(true))
                {
                    if (!HeroParts.IsWeapon(renderer.name) || renderer.name.IndexOf(part, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (renderer.gameObject.activeInHierarchy == show) continue;
                    renderer.gameObject.SetActive(show);
                    StirWeaponGuard(_actors[i]);
                    _weaponWatch[_actors[i]].StirredAt = Time.time - GuardIdleAfter;
                    return renderer.name + (show ? " switched on" : " switched off") + " on " + _actors[i].name;
                }
                return "no such weapon mesh to switch on " + _actors[i].name;
            }
            return "nobody stands at that rank";
        }
    }
}
