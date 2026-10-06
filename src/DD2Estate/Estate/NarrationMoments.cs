using System;
using DD2Estate.Dd2;
using DD2Estate.Dungeon;
using UnityEngine;

namespace DD2Estate.Estate
{
    /// <summary>
    /// Moments of an estate session that nothing announces: the hamlet coming up, an expedition appearing, its
    /// goal being reached, a hero falling. The narration and the town events hang on them. They are found by
    /// watching the public state of the systems that own them (HamletScreen.IsOpen, DungeonRun.Current, the
    /// graveyard's length) once a frame, so those systems need no calls into this one.
    /// </summary>
    [EstateModule]
    internal static class NarrationMoments
    {
        /// <summary>The hamlet screen came up: a new estate, a return from an expedition, a loaded save.</summary>
        public static event Action HamletOpened;

        /// <summary>
        /// A dungeon run is there that was not a frame ago: a fresh expedition or one restored from a save
        /// (listeners tell the two apart by the map's seed).
        /// </summary>
        public static event Action<DungeonRun> ExpeditionSeen;

        /// <summary>The quest's goal has just been reached, inside the dungeon.</summary>
        public static event Action<DungeonRun> ObjectiveCompleted;

        /// <summary>A hero died (the graveyard has just taken the record).</summary>
        public static event Action<Graveyard.Fallen> HeroFell;

        /// <summary>Once a frame while an estate session runs, after the moments above.</summary>
        public static event Action Tick;

        /// <summary>The session went back to the main menu.</summary>
        public static event Action SessionEnded;

        private static void Register()
        {
            // The same kind of object the plugin runs on: the game destroys the loader's own (MODLOG, gotcha 1).
            var go = new GameObject("DD2Estate.Moments") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Watcher>();
        }

        private class Watcher : MonoBehaviour
        {
            private bool _session, _hamlet, _objective;
            private DungeonRun _run;
            private int _graves = -1;

            private void Update()
            {
                if (!EstateSession.Active)
                {
                    if (!_session) return;
                    _session = _hamlet = _objective = false;
                    _run = null;
                    _graves = -1;
                    Raise(SessionEnded, "SessionEnded");
                    return;
                }
                _session = true;

                // While a save loads the graveyard fills up and an expedition may be restored: nobody dies then.
                var graves = Graveyard.All.Count;
                if (EstateSession.Starting || _graves < 0 || graves < _graves) _graves = graves;
                for (; _graves < graves; _graves++)
                {
                    var fallen = Graveyard.All[_graves];
                    try { HeroFell?.Invoke(fallen); }
                    catch (Exception e) { Plugin.Log.LogError("NarrationMoments: a HeroFell handler failed: " + e); }
                }
                if (EstateSession.Starting) return;

                var hamlet = HamletScreen.IsOpen;
                if (hamlet && !_hamlet) Raise(HamletOpened, "HamletOpened");
                _hamlet = hamlet;

                var run = DungeonRun.Current;
                if (!ReferenceEquals(run, _run))
                {
                    _run = run;
                    _objective = run != null && run.Exploration.ObjectiveComplete;
                    if (run != null) Raise(ExpeditionSeen, run, "ExpeditionSeen");
                }
                else if (run != null && !_objective && run.Exploration.ObjectiveComplete)
                {
                    _objective = true;
                    Raise(ObjectiveCompleted, run, "ObjectiveCompleted");
                }

                Raise(Tick, "Tick");
            }
        }

        private static void Raise(Action moment, string name)
        {
            try { moment?.Invoke(); }
            catch (Exception e) { Plugin.Log.LogError("NarrationMoments: a " + name + " handler failed: " + e); }
        }

        private static void Raise(Action<DungeonRun> moment, DungeonRun run, string name)
        {
            try { moment?.Invoke(run); }
            catch (Exception e) { Plugin.Log.LogError("NarrationMoments: a " + name + " handler failed: " + e); }
        }
    }
}
