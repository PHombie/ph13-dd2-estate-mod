using System;
using System.Collections;
using System.Collections.Generic;
using Assets.Code.Actor;
using Assets.Code.CommonLogic.Presentation;
using Assets.Code.Inn.Presentation;
using Assets.Code.Loading;
using Assets.Code.Presentation;
using Assets.Code.Utils;
using Cinemachine;
using DD2Estate.Dd2;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// The camp as DD2's Kingdoms shows a rest stop: the game's own "camp" scene (the fire, the seats, the
    /// night around them) with the party's hero models sitting at it.
    ///
    /// Kingdoms shows that scene by entering its INN mode on a camp cell of the kingdom map. The Estate has no
    /// kingdom map (InnBhv and InnSystem ask it for the current cell), no inn to visit, and DD1's meal and
    /// respite instead of DD2's rest items, so the mode is not entered: the scene alone is loaded, additively,
    /// the way InnBhv itself loads it, while the Estate's hub mode goes on. Outside INN mode the scene's own
    /// screens switch themselves off (InnPresentationBhv.Initialize, InnUiBhv.OnGameModeEnterStart); what
    /// stays is the set, its lights and its cameras. The heroes are put on the scene's spawn points with the
    /// call and the animator state its own PartyPresentationBhv uses, and a camera of the mod's own takes the
    /// scene's default camera pose, lowered so that the fire sits above the dungeon HUD.
    ///
    /// Everything here is presentation: a camp works without it (a scene that does not load leaves the camp
    /// screen over a dark room).
    /// </summary>
    internal static class CampView
    {
        /// <summary>InnBhv.SCENE_CAMP.</summary>
        public const string SceneName = "camp";

        // Tunables (world units), adjustable live through the dev bridge (camp.view).
        /// <summary>How far the camera sits below the scene's own: the HUD covers the bottom third of the screen.</summary>
        public static float CameraDrop = 0.3f;
        public static float CameraPan = 0f;
        public static float FovScale = 1f;
        public static float Timeout = 20f;

        /// <summary>
        /// The arena of a night ambush: Kingdoms' own camp ambush arena, the place the player was just looking
        /// at. Empty: the dungeon's own arena for a hallway fight.
        /// </summary>
        public static string AmbushArena = "combat_arena_kingdom_camp_ambush";

        /// <summary>hidden, loading, shown, failed, unloading.</summary>
        public static string State { get; private set; } = "hidden";

        private static readonly List<ActorBhv> Actors = new List<ActorBhv>();
        private static int _generation;
        private static Scene _previousActive;
        private static GameObject _camera;
        private static Transform _cameraSource;
        private static bool _foregroundAdded;

        public static bool Ready => State == "shown" || State == "failed";

        /// <summary>The models seated at the rest stop, in the party's order (<see cref="CampHeroes"/> lays the pointer's rectangles over them).</summary>
        internal static IReadOnlyList<ActorBhv> Seated => Actors;

        /// <summary>Loads the rest stop and seats the party. Returns at once; <see cref="State"/> tells how far it is.</summary>
        public static void Show()
        {
            _generation++;
            Plugin.Host.StartCoroutine(Guarded(ShowRoutine(_generation), "show"));
        }

        /// <summary>Takes the rest stop away; done when the scene is gone (or never came).</summary>
        public static IEnumerator Hide()
        {
            _generation++;
            return Guarded(HideRoutine(), "hide");
        }

        /// <summary>As <see cref="Hide"/>, for a caller that cannot wait (the session is leaving the hub).</summary>
        public static void HideNow()
        {
            if (State == "hidden") return;
            Plugin.Host.StartCoroutine(Hide());
        }

        private static RedHookSceneManagerBhv Scenes => SingletonMonoBehaviour<RedHookSceneManagerBhv>.Instance;

        // A coroutine that throws would leave the camp half shown for good; the failure is logged and the view
        // is marked failed, which the camp screen treats as "no backdrop".
        private static IEnumerator Guarded(IEnumerator routine, string what)
        {
            while (true)
            {
                bool more;
                try { more = routine.MoveNext(); }
                catch (Exception e)
                {
                    Plugin.Log.LogError("Camp view: " + what + " failed: " + e);
                    State = what == "hide" ? "hidden" : "failed";
                    yield break;
                }
                if (!more) yield break;
                yield return routine.Current;
            }
        }

        private static IEnumerator ShowRoutine(int generation)
        {
            State = "loading";
            // an earlier camp's scene may still be on its way out
            var waited = 0f;
            while (Scenes.UnloadOperationsInFlight() && waited < Timeout)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            if (generation != _generation) yield break;

            if (!Scenes.IsSceneLoaded(SceneName))
            {
                _previousActive = SceneManager.GetActiveScene();
                // InnBhv.GameModeEnterAsyncPreStart loads its scene with the same call; setActive makes the
                // scene the active one, which its rendering root waits for (SetActiveWhenSceneActiveBhv).
                RedHookSceneManagerBhv.LoadSceneAdditively(SceneName, Plugin.Host, setActive: true);
            }
            waited = 0f;
            while (!Scenes.IsSceneLoaded(SceneName) || !Globals.ArePendingInstallersComplete())
            {
                waited += Time.unscaledDeltaTime;
                if (waited > Timeout)
                {
                    Plugin.Log.LogWarning("Camp view: the game's camp scene did not load in " + Timeout + " s; camping goes on without it");
                    State = "failed";
                    yield break;
                }
                yield return null;
            }
            // the scene's own Start calls, and the scene manager making it the active scene
            yield return null;
            yield return null;
            if (generation != _generation) yield break;

            var scene = SceneManager.GetSceneByName(SceneName);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                State = "failed";
                yield break;
            }
            if (SceneManager.GetActiveScene() != scene) SceneManager.SetActiveScene(scene);
            Build(scene);
            State = "shown";
        }

        private static void Build(Scene scene)
        {
            SpawnPositions spawn = null;
            PartyPresentationBhv party = null;
            BlendActiveCameraBhv blend = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                // Out of INN mode the inn's own screen puts itself away; it is done here too, in case the scene
                // was loaded before its systems were told the mode.
                var presentation = root.GetComponentInChildren<InnPresentationBhv>(true);
                if (presentation != null && presentation.gameObject.activeSelf) presentation.gameObject.SetActive(false);
                if (spawn == null) spawn = root.GetComponentInChildren<SpawnPositions>(true);
                if (party == null) party = root.GetComponentInChildren<PartyPresentationBhv>(true);
                if (blend == null) blend = root.GetComponentInChildren<BlendActiveCameraBhv>(true);
            }

            // The set is switched on by being in the active scene; if that message was missed, by hand.
            if (spawn != null)
                for (var t = spawn.transform; t != null; t = t.parent)
                    if (!t.gameObject.activeSelf && t.GetComponent<InnPresentationBhv>() == null) t.gameObject.SetActive(true);

            // InnPresentationBhv.OnGameModeEnterStart: the inn draws its foreground props (the fire pit) by
            // adding their layer to the main camera for as long as it is shown.
            var main = Camera.main;
            var layer = LayerMask.NameToLayer("Foreground");
            if (main != null && layer >= 0 && (main.cullingMask & (1 << layer)) == 0)
            {
                main.cullingMask |= 1 << layer;
                _foregroundAdded = true;
            }

            _cameraSource = blend != null ? blend.transform.Find("Default Cam") : null;
            // shown again over a scene that never left: one camera, not two
            if (_camera != null) UnityEngine.Object.Destroy(_camera);
            _camera = new GameObject("DD2Estate.CampCamera");
            SceneManager.MoveGameObjectToScene(_camera, scene);
            var vcam = _camera.AddComponent<CinemachineVirtualCamera>();
            // above everything the scene and the corridor have
            vcam.Priority = 1000;
            ApplyCamera();
            if (_cameraSource == null)
            {
                // no default camera to copy: a camera of the mod's own would look at nothing, so the scene's
                // blend camera leads instead
                UnityEngine.Object.Destroy(_camera);
                _camera = null;
                if (blend != null) blend.Priority = 1000;
                Plugin.Log.LogWarning("Camp view: the scene has no 'Default Cam' to frame the camp by");
            }

            Seat(spawn, party != null ? party.DefaultAnimatorState : "inn_idle");
            Plugin.Log.LogInfo("Camp view: scene '" + SceneName + "' shown, " + Actors.Count + " heroes seated" + (spawn == null ? " (no spawn points found)" : ""));
        }

        /// <summary>Puts the camera where the tunables say (dev bridge: after changing them).</summary>
        public static void ApplyCamera()
        {
            if (_camera == null) return;
            var vcam = _camera.GetComponent<CinemachineVirtualCamera>();
            if (_cameraSource == null) return;
            var source = _cameraSource.GetComponent<CinemachineVirtualCamera>();
            _camera.transform.SetPositionAndRotation(_cameraSource.position + _cameraSource.right * CameraPan - _cameraSource.up * CameraDrop, _cameraSource.rotation);
            if (source != null) vcam.m_Lens = source.m_Lens;
            vcam.m_Lens.FieldOfView *= FovScale;
        }

        // PartyPresentationBhv.SpawnActorByActorGuid: the hero's model as a child of a spawn point, on the
        // spawn point's layer, in the state the scene's presentation names.
        private static void Seat(SpawnPositions spawn, string animatorState)
        {
            ClearActors();
            if (spawn == null) return;
            var positions = spawn.GetSpawnPositions();
            var creator = SingletonMonoBehaviour<ActorCreateGameObjectBhv>.Instance;
            var seat = 0;
            foreach (var hero in Provisioning.Party())
            {
                if (seat >= positions.Count) break;
                var at = positions[seat++];
                if (at == null) continue;
                var model = creator.CreateActorGameObject(hero.ActorGuid, at, at.gameObject.layer, animatorState, false);
                if (model != null) Actors.Add(model);
            }
        }

        private static void ClearActors()
        {
            foreach (var actor in Actors)
                if (actor != null) UnityEngine.Object.Destroy(actor.gameObject);
            Actors.Clear();
        }

        private static IEnumerator HideRoutine()
        {
            // a load still in flight has to land before it can be undone
            var waited = 0f;
            while (Scenes.IsSceneLoading(SceneName) && !Scenes.IsSceneLoaded(SceneName) && waited < Timeout)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            ClearActors();
            if (_camera != null) UnityEngine.Object.Destroy(_camera);
            _camera = null;
            _cameraSource = null;
            if (_foregroundAdded)
            {
                // InnPresentationBhv.OnGameModeExitStart
                var main = Camera.main;
                var layer = LayerMask.NameToLayer("Foreground");
                if (main != null && layer >= 0) main.cullingMask &= ~(1 << layer);
                _foregroundAdded = false;
            }

            if (Scenes.IsSceneLoaded(SceneName))
            {
                State = "unloading";
                // The scene manager parks on a temporary scene when asked to unload the active one, and the
                // Estate's modes never clear that away (MODLOG gotcha 9): the hub's scene is made active first.
                var scene = SceneManager.GetSceneByName(SceneName);
                if (scene.IsValid() && SceneManager.GetActiveScene() == scene)
                {
                    var back = _previousActive.IsValid() && _previousActive.isLoaded ? _previousActive : EstateSession.Scene;
                    if (back.IsValid() && back.isLoaded) SceneManager.SetActiveScene(back);
                }
                // InnBhv.OnGameModeExitComplete
                RedHookSceneManagerBhv.UnloadAdditiveSceneByForce(SceneName);
                waited = 0f;
                while ((Scenes.IsSceneLoaded(SceneName) || Scenes.UnloadOperationsInFlight()) && waited < Timeout)
                {
                    waited += Time.unscaledDeltaTime;
                    yield return null;
                }
                if (waited >= Timeout) Plugin.Log.LogWarning("Camp view: the camp scene did not unload in " + Timeout + " s");
            }
            State = "hidden";
        }
    }
}
