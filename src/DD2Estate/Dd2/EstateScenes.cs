using System;
using System.Collections.Generic;
using System.Reflection;
using Assets.Code.Combat;
using Assets.Code.Combat.Presentation;
using Assets.Code.Game;
using Assets.Code.Game.StageCoach;
using Assets.Code.Loading;
using Assets.Code.UI.Transitions;
using Assets.Code.Utils;
using DD2Estate.Dev;
using DD2Estate.Estate;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DD2Estate.Dd2
{
    /// <summary>
    /// DD2's own scenery has no place in an Estate session: an expedition of DD1 has no stagecoach, and the party
    /// does not stand on DD2's road between a fight and the corridor it was fought in.
    ///
    /// <list type="bullet">
    /// <item><b>After a fight</b> DD2 goes to its RESULTS mode: the scene "combat_results", a timeline of the party
    /// standing before the stagecoach while the loot window is up (CombatResultsPresentationBhv), and from there
    /// to the road. In the Estate that scene had nothing to do (no loot of DD2's is granted: Dd2Loot), it only
    /// passed by. Here the game is sent from the fight straight to the Estate's hub (<see cref="AfterFight"/>):
    /// DD2's wipe closes over the fight's last frame and the corridor the party fought in comes up out of the
    /// black, with DD1's scroll of the fight's loot. What the results scene did besides standing there is done at
    /// that moment: the narrator's word on the fight is queued, the list of DD2's loot window is emptied, the
    /// scene is unloaded (as DD2 itself unloads it where it shows no results: a lost party, a siege).</item>
    /// <item><b>The coach itself</b>, wherever a scene of DD2's that the Estate shows has one. Found by the scene
    /// watch (Dev/SceneWatch.cs, tools/coach_watch.py, tools/arena_coach_survey.py) on a trip through every
    /// passage and a fight in every arena the mod names: the results scene ("scene_offset/stagecoach_art", loaded
    /// with every fight and so lying behind every arena), and 15 of the 48 arenas ("Art/stagecoach_art_arenas":
    /// the wagon and its horses behind the heroes): DD2's road-side arenas (*_faction, *_gaunt, *_pillager,
    /// city_military, the two barricades), which the Weald, the Warrens, the Cove and the Hamlet's incursion
    /// fight their hallway battles in, and Kingdoms' "combat_arena_kingdom_camp_ambush", a camp's night ambush.
    /// The dungeon interiors, the dens and the bosses' lairs have none; nor has the rest stop behind the camp's
    /// screen (the scene "camp").
    /// In all of them the coach is a model hung into the scene by StageCoachSkinSpawnerBhv (on "wagon_skin" under
    /// "stagecoach_art...", the horses on "horses_skin" beside it): it is not put up while an Estate session lasts
    /// (the spawner waits, <see cref="TheCoachStaysOutOfTheEstate"/>). Should a scene have a coach of its own art
    /// besides (none seen), whatever is named for it is switched off, renderer by renderer and light by light,
    /// and remembered (<see cref="Tick"/>), and switched on again when the session is over.</item>
    /// <item><b>A fight left for the hub by another way</b> (a lost party: RosterLifecycle.OnBattleResult) left the
    /// results scene loaded for the rest of the session, under the hub's screens; it is unloaded there too.</item>
    /// </list>
    /// Nothing of this reaches the regular game: all of it asks <see cref="EstateSession.Active"/>.
    /// </summary>
    [EstateModule]
    internal static class EstateScenes
    {
        /// <summary>[Scenes] AfterFight: "corridor" (from the fight straight back to the corridor) or "dd2" (DD2's results scene first).</summary>
        public static string AfterFight = "corridor";
        /// <summary>[Scenes] Dd2Coach: DD2's stagecoach is shown where DD2's scenes have it.</summary>
        public static bool ShowCoach;

        /// <summary>DD2's wipe out of a fight (the one it goes to its results with), and a fade up into the corridor.</summary>
        private static readonly SceneTransition FromFight = new SceneTransition(TransitionType.RIGHT_TO_LEFT, TransitionType.FADE);

        private static readonly FieldInfo ResultsScene = AccessTools.Field(typeof(CombatPresentationBhv), "m_CurrentCombatResultsScene");
        private static readonly MethodInfo ResultsNarration = AccessTools.Method(typeof(CombatResultsPresentationBhv), "HandleResultsNarration");
        private static readonly FieldInfo HorseField = AccessTools.Field(typeof(StageCoachSkinSpawnerBhv), "m_HorseGameObject");

        // a scene's own coach art, found by name once the scene has come (and once more a moment later)
        private class Found
        {
            public readonly List<GameObject> Roots = new List<GameObject>();
            public float Again;
        }

        private static readonly Dictionary<int, Found> Coaches = new Dictionary<int, Found>();
        private static readonly List<Behaviour> HiddenLights = new List<Behaviour>();
        private static readonly List<Renderer> Hidden = new List<Renderer>();
        private static readonly HashSet<int> Known = new HashSet<int>();
        private static float _nextSweep;
        private static int _everyFrameUntil;
        private static bool _hooked;
        private static int _skipped;
        private static readonly HashSet<int> HeldBack = new HashSet<int>();
        private static string _lastSkip;

        private static void Register()
        {
            AfterFight = Plugin.Settings.Bind("Scenes", "AfterFight", "corridor",
                "What follows a fight of an expedition: corridor = straight back to the corridor or room the party fought in (DD1 has no other place), dd2 = DD2's own results scene first (the party before the stagecoach).").Value.Trim().ToLowerInvariant();
            ShowCoach = Plugin.Settings.Bind("Scenes", "Dd2Coach", false,
                "DD2's stagecoach in the scenes of DD2 the Estate shows (a fight's arena, the rest stop behind the camp). Off: an expedition has no coach, as in DD1.").Value;

            LoadBehindWipe = Plugin.Settings.Bind("Scenes", "FightLoadsBehindWipe", true,
                "A fight's arena is loaded when DD2's wipe has closed over the corridor (as DD2 does for a fight on its road). Off: under the wipe as it closes, as before: the fight may stand a moment sooner, and the corridor's last half second is blurred and lit by the arena that is already there.").Value;

            AgentBridge.Register("scenes.state", o =>
            {
                if (o["afterfight"] != null) AfterFight = ((string)o["afterfight"]).Trim().ToLowerInvariant();
                if (o["behindwipe"] != null) LoadBehindWipe = (bool)o["behindwipe"];
                if (o["coach"] != null)
                {
                    ShowCoach = (bool)o["coach"];
                    _nextSweep = 0f;
                }
                return Describe();
            });
        }

        /// <summary>A fight's end leads straight to the hub.</summary>
        public static bool SkipsResults => AfterFight != "dd2";

        /// <summary>The coach is kept out of DD2's scenes: for as long as an Estate session lasts, the way out to the main menu excepted.</summary>
        public static bool HidesCoach
        {
            get
            {
                if (ShowCoach || !EstateSession.Active) return false;
                var modes = Singleton<GameModeMgr>.Instance;
                return GameModeMgr.CurrentMode != GameModeType.MAIN_MENU && !(modes.IsChangingState() && modes.GetNextMode() == GameModeType.MAIN_MENU);
            }
        }

        // ---- into a fight ---------------------------------------------------------------------------------------

        /// <summary>[Scenes] FightLoadsBehindWipe: the fight's scenes are loaded when the wipe has closed over the hub.</summary>
        public static bool LoadBehindWipe = true;
        private static int _lateLoads;
        private static string _lastLoad;

        /// <summary>
        /// The game goes from the hub into a fight. DD2's wipe (ink from the top, 0.9 s to black) closes over
        /// the corridor, the room, the camp or the hamlet; the fight stands when it lifts.
        ///
        /// A scenario that is "loaded" as it is set (GameTypeMgr.SetCombatScenario(.., isLoad: true), which is
        /// how the Estate began its fights) asks for the scene "combat" and the arena's scene in that very
        /// frame, under the wipe that has only begun. Some ten frames later they are in, a good half second
        /// before the ink has covered anything, and with them the arena's own global post volume ("Post
        /// Processing": a bokeh depth of field of 248 mm at f/8.7 focused at 12.3, bloom, heavy grain), which
        /// takes the main camera at once. That camera is still drawing the corridor: walls and floor (sprites
        /// that write no depth, so the lens "focuses" on whatever the arena put into the depth buffer behind
        /// them) smear sideways in columns and the arena's lights glow through at the screen's edges, while
        /// the heroes (the character camera, after the post effects) and the HUD stay sharp on top: the torn
        /// picture of the owner's screenshot 40, some forty frames of it before the ink is over it.
        ///
        /// DD2's own fights on the road do not do this: TriggerCombatBhv sets its scenario in
        /// OnGameModeExitComplete, when the mode being left has gone under the wipe. The same here: the
        /// scenario is set without loading and loaded when the hub's exit is complete (the fader is black:
        /// the wipe's own signal ends the exit). The mode change then waits for the scenes behind the black,
        /// with DD2's sign in the corner, as it does for a road fight.
        /// </summary>
        public static void BeginFight(CombatScenarioData scenario)
        {
            var modes = Singleton<GameModeMgr>.Instance;
            var late = LoadBehindWipe;
            Singleton<GameTypeMgr>.Instance.SetCombatScenario(scenario, isLoad: !late);
            if (late)
            {
                var asked = Time.frameCount;
                modes.OnNextGameModeExitComplete(left =>
                {
                    try
                    {
                        // (an exit that is not this fight's: the way to the fight was cut off, and nothing is to be loaded)
                        if (modes.GetNextMode() != GameModeType.COMBAT || scenario.IsLoadStarted) return;
                        _lateLoads++;
                        _lastLoad = "asked in frame " + asked + ", loaded in frame " + Time.frameCount + " (" + (Time.frameCount - asked) + " later), fader "
                                    + (SingletonMonoBehaviour<Assets.Code.UI.ScreenFaderBhv>.HasInstance() && SingletonMonoBehaviour<Assets.Code.UI.ScreenFaderBhv>.Instance.FadingToBlack ? "closing" : "black");
                        Singleton<GameTypeMgr>.Instance.LoadCombatScenario();
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogError("Estate: the fight's scenes could not be loaded behind the wipe: " + e);
                    }
                });
            }
            modes.SetMode(GameModeType.COMBAT, isLoad: false);
        }

        // ---- after a fight --------------------------------------------------------------------------------------

        /// <summary>
        /// DD2 asks for its RESULTS mode (GameModeMgr.SetMode, in an Estate session). True: the hub is to be the
        /// mode instead, by <paramref name="transition"/>; what the results scene would have done is done.
        /// </summary>
        public static bool OnResultsAsked(ref SceneTransition transition)
        {
            if (!SkipsResults) return false;
            _skipped++;
            _lastSkip = "frame " + Time.frameCount;
            try
            {
                var presentation = SingletonMonoBehaviour<ArenaBhv>.HasInstance() ? SingletonMonoBehaviour<ArenaBhv>.Instance.CombatPresentation : null;
                // the narrator's word on the fight, which DD2 speaks as its results scene opens
                if (presentation != null && presentation.RegisteredCombatResultsPresentation is CombatResultsPresentationBhv results && results.IsConfigured)
                {
                    try { ResultsNarration?.Invoke(results, null); }
                    catch (Exception e) { Plugin.Log.LogWarning("Estate: DD2's word on the fight could not be queued: " + (e.InnerException ?? e).Message); }
                }
                _lastSkip += UnloadResults();
                // (LootManager.ShowLoot ends with this; with nothing granted there is no window to show)
                Singleton<GameTypeMgr>.Instance.LootManager.ClearShowWindowVariables();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Estate: leaving the fight for the corridor: " + e);
            }
            if (transition == null) transition = FromFight;
            Plugin.Log.LogInfo("Estate: no results scene of DD2's after the fight; straight to the hub (" + _lastSkip + ")");
            return true;
        }

        /// <summary>
        /// The hub is asked for out of a fight by a way that is not the results scene's (a lost party): the
        /// results scene, loaded with the fight, is in nobody's care any more.
        /// </summary>
        public static void OnFightLeftForHub()
        {
            try
            {
                var unloaded = UnloadResults();
                if (unloaded.Length > 0) Plugin.Log.LogInfo("Estate: the fight is left for the hub" + unloaded);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Estate: the results scene of a fight left behind: " + e);
            }
        }

        // DD2 does the same wherever it shows no results (CombatPresentationBhv.CheckForEndOfCombat: a lost
        // party, a siege; GameModeExitAsyncPreStart: out to the main menu).
        private static string UnloadResults()
        {
            var presentation = SingletonMonoBehaviour<ArenaBhv>.HasInstance() ? SingletonMonoBehaviour<ArenaBhv>.Instance.CombatPresentation : null;
            if (presentation == null || !(ResultsScene?.GetValue(presentation) is string scene) || string.IsNullOrEmpty(scene)) return "";
            RedHookSceneManagerBhv.UnloadAdditiveSceneByForce(scene);
            ResultsScene.SetValue(presentation, null);
            return ", '" + scene + "' unloaded";
        }

        // ---- the coach ------------------------------------------------------------------------------------------

        /// <summary>Every frame (EstateSession.Tick): what a scene has of the coach is switched off, or given back.</summary>
        public static void Tick()
        {
            if (!_hooked)
            {
                _hooked = true;
                // a scene that has just come is looked at every frame for a while: its coach may be put up a moment later
                SceneManager.sceneLoaded += (scene, mode) => _everyFrameUntil = Time.frameCount + 300;
                SceneManager.sceneUnloaded += scene => Coaches.Remove(scene.handle);
            }
            if (!HidesCoach)
            {
                Restore();
                return;
            }
            if (Time.frameCount > _everyFrameUntil && Time.unscaledTime < _nextSweep) return;
            _nextSweep = Time.unscaledTime + 0.5f;
            try { Sweep(); }
            catch (Exception e) { Plugin.Log.LogWarning("Estate: the coach could not be looked for: " + e.Message); }
        }

        private static void Sweep()
        {
            var own = EstateSession.Scene;
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded || scene == own) continue;
                if (!Coaches.TryGetValue(scene.handle, out var found))
                {
                    Coaches[scene.handle] = found = new Found { Again = Time.unscaledTime + 1.5f };
                    Scan(scene, found);
                }
                else if (found.Again > 0f && Time.unscaledTime >= found.Again)
                {
                    found.Again = 0f;
                    Scan(scene, found);
                }
                foreach (var root in found.Roots) Hide(root);
            }
            foreach (var spawner in UnityEngine.Object.FindObjectsOfType<StageCoachSkinSpawnerBhv>())
            {
                if (spawner == null) continue;
                Hide(spawner.gameObject);
                // the horses stand in the scene itself, beside the place the coach's model is hung into
                if (HorseField?.GetValue(spawner) is GameObject horse && horse != null) Hide(horse);
            }
            foreach (var wagon in UnityEngine.Object.FindObjectsOfType<WagonBhv>()) Hide(wagon.gameObject);
            foreach (var horse in UnityEngine.Object.FindObjectsOfType<HorseBhv>()) Hide(horse.gameObject);
        }

        // The parts of a scene's art that are the coach: the topmost objects named for it.
        private static void Scan(Scene scene, Found found)
        {
            found.Roots.Clear();
            foreach (var root in scene.GetRootGameObjects()) Scan(root.transform, found.Roots);
        }

        private static void Scan(Transform t, List<GameObject> roots)
        {
            if (IsCoach(t.name))
            {
                roots.Add(t.gameObject);
                return;
            }
            for (var i = 0; i < t.childCount; i++) Scan(t.GetChild(i), roots);
        }

        private static bool IsCoach(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            var lower = name.ToLowerInvariant();
            return lower.Contains("stagecoach") || lower.Contains("stage_coach") || lower == "wagon_skin" || lower == "horses_skin";
        }

        private static void Hide(GameObject root)
        {
            if (root == null) return;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || !renderer.enabled) continue;
                renderer.enabled = false;
                if (Known.Add(renderer.GetInstanceID())) Hidden.Add(renderer);
            }
            foreach (var light in root.GetComponentsInChildren<Light>(true))
            {
                if (light == null || !light.enabled) continue;
                light.enabled = false;
                if (Known.Add(light.GetInstanceID())) HiddenLights.Add(light);
            }
        }

        private static void Restore()
        {
            if (Hidden.Count == 0 && HiddenLights.Count == 0) return;
            foreach (var renderer in Hidden)
                if (renderer != null) renderer.enabled = true;
            foreach (var light in HiddenLights)
                if (light != null) light.enabled = true;
            Hidden.Clear();
            HiddenLights.Clear();
            Known.Clear();
        }

        /// <summary>The coach's model was kept out of a scene (the patch below).</summary>
        internal static void Held(StageCoachSkinSpawnerBhv spawner) => HeldBack.Add(spawner.GetInstanceID());

        private static object Describe()
        {
            var hidden = new SortedDictionary<string, int>();
            foreach (var renderer in Hidden)
            {
                if (renderer == null) continue;
                var top = renderer.gameObject.scene.name + ": " + Top(renderer.transform);
                hidden[top] = (hidden.TryGetValue(top, out var n) ? n : 0) + 1;
            }
            var named = new List<string>();
            foreach (var pair in Coaches)
                foreach (var root in pair.Value.Roots)
                    if (root != null) named.Add(root.scene.name + ": " + Path(root.transform));
            var spawners = new List<object>();
            foreach (var spawner in Resources.FindObjectsOfTypeAll<StageCoachSkinSpawnerBhv>())
            {
                if (spawner == null || !spawner.gameObject.scene.IsValid()) continue;
                spawners.Add(new
                {
                    scene = spawner.gameObject.scene.name, path = Path(spawner.transform), active = spawner.isActiveAndEnabled, spawned = spawner.HasSpawnedWagon(),
                    type = Convert.ToString(AccessTools.Field(typeof(StageCoachSkinSpawnerBhv), "m_prefabType")?.GetValue(spawner)),
                    horse = HorseField?.GetValue(spawner) is GameObject horse && horse != null ? Path(horse.transform) : null
                });
            }
            return new
            {
                afterFight = AfterFight, skipsResults = SkipsResults, resultsSkipped = _skipped, lastSkip = _lastSkip,
                fightLoadsBehindWipe = LoadBehindWipe, fightsLoadedBehindWipe = _lateLoads, lastFightLoad = _lastLoad,
                showCoach = ShowCoach, hidesCoachNow = HidesCoach, coachesHeldBack = HeldBack.Count, hiddenRenderers = Hidden.Count, hiddenLights = HiddenLights.Count, hidden, named, spawners
            };
        }

        private static string Top(Transform t)
        {
            var chain = new List<string>();
            for (var p = t; p != null; p = p.parent) chain.Add(p.name);
            chain.Reverse();
            return string.Join("/", chain.GetRange(0, Mathf.Min(4, chain.Count)));
        }

        private static string Path(Transform t)
        {
            var path = t.name;
            for (var p = t.parent; p != null && path.Length < 200; p = p.parent) path = p.name + "/" + path;
            return path;
        }
    }

    /// <summary>
    /// The coach's model is hung into a scene by StageCoachSkinSpawnerBhv, in its first Update after the scene has
    /// come. While the Estate keeps the coach out that Update waits (the spawner stays as it was, and puts its
    /// coach up as soon as it may: a scene that outlives the session, the main menu's, gets its coach as ever).
    /// </summary>
    [HarmonyPatch(typeof(StageCoachSkinSpawnerBhv), "Update")]
    internal static class TheCoachStaysOutOfTheEstate
    {
        private static readonly FieldInfo Pending = AccessTools.Field(typeof(StageCoachSkinSpawnerBhv), "m_updateStageCoachSkin");

        private static bool Prefix(StageCoachSkinSpawnerBhv __instance)
        {
            if (!EstateScenes.HidesCoach) return true;
            if (Pending != null && Pending.GetValue(__instance) is bool pending && pending) EstateScenes.Held(__instance);
            return false;
        }
    }
}
