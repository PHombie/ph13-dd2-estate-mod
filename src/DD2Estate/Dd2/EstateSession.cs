using System;
using System.Collections;
using System.Collections.Generic;
using Assets.Code.Actor;
using Assets.Code.Combat;
using Assets.Code.Game;
using Assets.Code.Kingdom;
using Assets.Code.Library;
using Assets.Code.Roster;
using Assets.Code.Serialization;
using Assets.Code.Utils;
using DD2Estate.Dungeon;
using DD2Estate.Estate;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DD2Estate.Dd2
{
    /// <summary>
    /// One Estate playthrough as the game sees it: a Kingdom-type game (KingdomBhv owns the managers that
    /// combat, heroes and saving need) that never visits the kingdom map, inns or the road. The hamlet and the
    /// dungeon live in the mod's own game mode; only fights switch to the vanilla COMBAT/RESULTS modes.
    /// </summary>
    internal static class EstateSession
    {
        /// <summary>True from the moment the Estate is entered until the main menu is reached again.</summary>
        public static bool Active { get; private set; }

        public static bool Starting { get; private set; }

        public enum Screen { Hamlet, Dungeon }

        /// <summary>Which of the mod's screens owns the hub mode right now.</summary>
        public static Screen View = Screen.Hamlet;

        private static bool _dungeonShown, _dungeonLive;
        // on the way back from a fight that does not end where it began, until the hub is in (see Tick)
        private static bool _returning;
        private static GameModeType _lastMode;
        // on the way in from the main menu, until the hub's first screen is up (see EstateCurtain)
        private static bool _entering;
        private static float _nextSweep;
        private const string SceneName = "estate_hub";
        private static Scene _scene;

        /// <summary>The session's own empty scene; world-space objects of the mod belong here.</summary>
        public static Scene Scene => _scene;

        public static bool InHub => Active && GameModeMgr.CurrentMode == EstateMode.Hub && !Singleton<GameModeMgr>.Instance.IsChangingState();

        /// <summary>The hub is the game's mode, a change of mode under way or not: what the hub shows can be on
        /// screen then (under the game's fade), though nothing can be done in it until <see cref="InHub"/>.</summary>
        public static bool HubMode => Active && GameModeMgr.CurrentMode == EstateMode.Hub;

        public static IEnumerator Enter()
        {
            if (Active || Starting) yield break;
            Starting = true;
            try
            {
                if (!EstateProfile.Activate()) yield break;
                // (a session that begins without Darkest Dungeon builds its screens without it: a folder named
                // after that waits for a restart)
                DD2Estate.Dd1.Dd1Install.SessionBegins();

                // The main menu goes under the game's own wipe, as it does for a Kingdom, and the load is behind it
                // with the game's throbber; the mode change that follows finds the fader black and leaves it so.
                _entering = true;
                if (SingletonMonoBehaviour<Assets.Code.UI.ScreenFaderBhv>.HasInstance())
                    SingletonMonoBehaviour<Assets.Code.UI.ScreenFaderBhv>.Instance.TransitionToBlack(Assets.Code.UI.Transitions.TransitionType.BOTTOM_TO_TOP, showThrobber: true);

                Singleton<GameTypeMgr>.Instance.SetGameType(GameType.KINGDOM);

                // Same order as the vanilla "new Kingdom" flow: data libraries first, then the owner.
                var installer = Globals.MainInstaller.SystemsRoot.GetComponentInChildren<PostMainMenuSystemsInstaller>();
                installer.TryInstall();
                while (!installer.IsInstalled) yield return null;

                var resume = EstatePersistence.HasSave();
                var kingdom = SingletonMonoBehaviour<KingdomBhv>.Instance;
                Active = true;
                if (resume) yield return EstatePersistence.Continue();
                else
                {
                    EstatePersistence.StartNew();
                    yield return kingdom.StartKingdom(EstateMode.Hub, null, null, isLoad: false, loadModeByCell: false);
                }
                if (!kingdom.IsKingdomStarted || (resume && !EstatePersistence.Loaded))
                {
                    Plugin.Log.LogError("Estate: " + (EstatePersistence.LastError ?? "the game did not start its Kingdom-type session"));
                    Active = false;
                    EstateProfile.Deactivate();
                    yield break;
                }

                // Kingdoms loads the road scene for every "in biome" mode (COMBAT, RESULTS) unless a kingdom day is
                // in progress. A dormant day object keeps the road out; nothing advances it outside INN mode.
                var dayField = AccessTools.Field(typeof(KingdomBhv), "m_KingdomDaySystem");
                if (dayField.GetValue(kingdom) == null) dayField.SetValue(kingdom, new KingdomDaySystem());

                if (!resume)
                {
                    EnsureStartingRoster();
                    EstateState.StartNew();
                    // Kingdom rules start the purse at 0; DD1's hamlet is first seen with the Old Road's pay in it.
                    EstateState.AddGold(EstateState.StartingGold - EstateState.Gold, "a new estate");
                }

                // The hub mode has no scene of its own. The game parks on a "Temp Unload Scene" whenever the last
                // scene goes away and can only remove it again if another scene is loaded, so the session keeps
                // one empty scene alive (it also hosts the dungeon's world objects).
                if (!_scene.IsValid()) _scene = SceneManager.CreateScene(SceneName);

                // DD1 puts its loading screen up on the way into a save: the town's, or the dungeon's (which waits
                // for a key) when the save is on an expedition. It stands under the game's fader in the black
                // sheet's place and takes itself down when the hub's first screen stands. Without it (no DD1
                // picture) the black sheet does as before.
                if (LoadingScreen.ShowForSave())
                {
                    _entering = false;
                    EstateCurtain.Lift(0f);
                }

                // (a loading screen in DD2's look keeps the game's fade black when the mode is in and lifts it itself)
                Singleton<GameModeMgr>.Instance.SetMode(EstateMode.Hub, isLoad: false, LoadingScreen.HubEnterTransition());
                Plugin.Log.LogInfo(resume ? "Estate session continued" : "Estate session started");
            }
            finally
            {
                Starting = false;
                if (!Active && _entering)
                {
                    // the way in failed: the main menu is given back
                    _entering = false;
                    EstateCurtain.Lift(0.2f);
                    if (SingletonMonoBehaviour<Assets.Code.UI.ScreenFaderBhv>.HasInstance() && !SingletonMonoBehaviour<Assets.Code.UI.ScreenFaderBhv>.Instance.IsClear)
                        SingletonMonoBehaviour<Assets.Code.UI.ScreenFaderBhv>.Instance.FadeOutOfBlack(showThrobber: false);
                }
            }
        }

        public static void ExitToMenu()
        {
            if (!Active) return;
            // the estate is written as the mode is set (RedirectVanillaHubModes); the hamlet is closed by Tick, once
            // the fade has covered it
            Singleton<GameModeMgr>.Instance.SetMode(GameModeType.MAIN_MENU, isLoad: false);
        }

        /// <summary>Called every frame by the host: keeps the mod's screens in step with the game mode.</summary>
        public static void Tick()
        {
            var mode = GameModeMgr.CurrentMode;
            var hub = HubMode;
            var cameFrom = _lastMode;
            _lastMode = mode;
            EstateCurtain.Tick();
            // the mod's black sheet goes down behind the game's fade while that is black, and stays when it lifts
            if (_entering && !EstateCurtain.Down && SingletonMonoBehaviour<Assets.Code.UI.ScreenFaderBhv>.HasInstance()
                && SingletonMonoBehaviour<Assets.Code.UI.ScreenFaderBhv>.Instance.IsBlack) EstateCurtain.Drop(0f);
            EstateSky.Keep(Active || Starting);
            // nothing of the game's world is in view in the hub: its fog has nothing to lie on and its blur nothing
            // to blur (see EstateSky)
            EstateSky.Fog(!hub);
            EstateSky.Veil(hub);
            // DD2's stagecoach is kept out of the scenes an expedition shows, and given back after the session
            EstateScenes.Tick();
            // the dark a camp holds before its night ambush lies over everything the game draws: never outside the hub
            if (!hub && CampScreen.DarkHeld) CampScreen.LiftDark();
            if (!Active) return;
            Dd2Torch.Prepare();
            if (mode == GameModeType.MAIN_MENU && !Starting && !Singleton<GameModeMgr>.Instance.IsChangingState())
            {
                // The main menu ends the Kingdom-type session; hand the player's own profile selection back.
                Active = false;
                View = Screen.Hamlet;
                Difficulty.Clear();
                DungeonRun.Current?.Dispose();
                Dd2Loot.Clear();
                CorridorView.Suspend();
                DungeonHud.Hide();
                _dungeonShown = _dungeonLive = false;
                if (_returning)
                {
                    _returning = false;
                    EstateCurtain.Lift(0f);
                }
                HamletScreen.Close();
                RaidResultsScreen.Close();
                if (_scene.IsValid()) SceneManager.UnloadSceneAsync(_scene);
                _scene = default;
                EstateProfile.Deactivate();
                Plugin.Log.LogInfo("Estate session ended");
                return;
            }
            // The game changes mode behind its fade, and the hub is not "in" (InHub) from the first frame of that
            // fade to the last. What the hub shows stays for as long as it can be seen: under the fade as it closes
            // over it (on the way to a fight or to the main menu), and the dungeon under it again as it lifts on the
            // way back. Nothing can be done there meanwhile: every action asks InHub.
            var covered = SingletonMonoBehaviour<Assets.Code.UI.ScreenFaderBhv>.HasInstance() && SingletonMonoBehaviour<Assets.Code.UI.ScreenFaderBhv>.Instance.IsBlack;
            var underFade = hub && !covered;

            var hamlet = View == Screen.Hamlet && (InHub || (underFade && HamletScreen.IsOpen));
            if (hamlet && !HamletScreen.IsOpen) HamletScreen.Open();
            else if (!hamlet && HamletScreen.IsOpen) HamletScreen.Close();

            var dungeon = InHub && View == Screen.Dungeon;
            // on the way back the view is put up early only where that is all there is to it: not over a camp, which
            // takes the corridor's place again, not an expedition out of a save, which has yet to be put up, not
            // after a fight nobody came back from, and not after a flight, which ends in another place (the room
            // showed under the fade and the hallway took its place a moment later)
            var run = DungeonRun.Current;
            var early = run != null && run.Camp == null && !run.AwaitsResume && run.StaysWhereItFought && !PartyDead();
            var seen = dungeon || (underFade && View == Screen.Dungeon && !RaidResultsScreen.IsOpen && (_dungeonShown || early));
            // Where the view cannot be put up early (a flight; a lost party, whose way back ends in the results
            // screens) the game's fade used to lift over nothing, and the hallway or the results popped in after
            // it. The mod's black sheet goes down as the hub becomes the game's mode, which it does behind the
            // game's fade, and is lifted once the hub is in and its screen is up: that screen comes up out of the
            // dark, as the corridor does after a fight won.
            if (hub && mode != cameFrom && (cameFrom == GameModeType.COMBAT || cameFrom == GameModeType.RESULTS)
                && View == Screen.Dungeon && !_entering && !early && run != null)
            {
                _returning = true;
                EstateCurtain.Drop(0f);
            }
            if (seen != _dungeonShown)
            {
                _dungeonShown = seen;
                if (seen)
                {
                    CorridorView.Resume();
                    DungeonHud.Show();
                }
                else
                {
                    CorridorView.Suspend();
                    DungeonHud.Hide();
                }
            }
            if (dungeon != _dungeonLive)
            {
                _dungeonLive = dungeon;
                if (dungeon) DungeonRun.Current?.OnReturnedFromFight();
            }
            // what DD2 itself dropped into its player inventory belongs to the estate's ledgers (Dd2Sweep)
            if (InHub && Time.unscaledTime >= _nextSweep)
            {
                _nextSweep = Time.unscaledTime + 0.5f;
                Dd2Sweep.InHub();
            }
            // the hub's first screen is up: it comes up out of the dark
            if (_entering && (HamletScreen.IsOpen || _dungeonLive))
            {
                _entering = false;
                EstateCurtain.Lift(EnterFadeIn);
            }
            // back from a fight that ended elsewhere: the hub is in, and what follows the fight has been put up
            if (_returning && (InHub || !hub))
            {
                _returning = false;
                EstateCurtain.Lift(hub ? ReturnFadeIn : 0f);
            }
        }

        /// <summary>How long the hamlet (or the expedition out of a save) takes to come up out of the dark on the way in.</summary>
        public static float EnterFadeIn = 0.7f;

        /// <summary>How long the hallway (after a flight) or the results (after a lost fight) take to come up out of the dark.</summary>
        public static float ReturnFadeIn = 0.4f;

        // ---- roster ---------------------------------------------------------------------------------

        private static RosterManager Roster => Singleton<GameTypeMgr>.Instance.RosterManager;

        // On the way out of the Estate with an expedition under way the game's roster is gone a frame or two
        // before the session is (seen: a NullReferenceException in Tick twice at every such exit).
        private static bool PartyDead()
        {
            var types = Singleton<GameTypeMgr>.Instance;
            var roster = types != null ? types.RosterManager : null;
            return roster != null && roster.GetIsPartyDead();
        }

        // DD1 starts small: four heroes, the rest arrive by stage coach.
        private static void EnsureStartingRoster() => RosterLifecycle.CreateStartingRoster();

        public static IReadOnlyList<uint> RosterGuids() => AllGuids(Roster);

        public static bool IsInParty(uint guid) => Roster.GetIsActorInParty(guid);

        public static int PartySize => Roster.GetActorGuids(RosterStatusType.PARTY).Count;

        /// <summary>Reasons a hero cannot embark (in treatment, missing, ...): each returns a short text or null.
        /// Systems add their own check at start-up.</summary>
        public static readonly List<Func<uint, string>> PartyBlockers = new List<Func<uint, string>>();

        public static string PartyBlockReason(uint guid)
        {
            foreach (var blocker in PartyBlockers)
            {
                var reason = blocker(guid);
                if (!string.IsNullOrEmpty(reason)) return reason;
            }
            return null;
        }

        /// <summary>Hamlet roster click: bench a party member, or add a benched hero if a slot is free.</summary>
        public static void ToggleParty(uint guid)
        {
            if (!InHub || View != Screen.Hamlet) return;
            if (IsInParty(guid)) SetStatus(Roster, guid, RosterStatusType.IDLE);
            else if (PartySize < RosterManager.FULL_PARTY_SIZE && PartyBlockReason(guid) == null) SetStatus(Roster, guid, RosterStatusType.PARTY);
        }

        /// <summary>Takes a hero out of the party regardless of the current screen (e.g. sent to an activity).</summary>
        public static void Bench(uint guid)
        {
            if (Active && IsInParty(guid)) SetStatus(Roster, guid, RosterStatusType.IDLE);
        }

        private static IReadOnlyList<uint> AllGuids(RosterManager roster) => roster.GetActorGuids((Predicate<RosterStatusType>)(s => true));

        private static void SetStatus(RosterManager roster, uint guid, RosterStatusType status)
        {
            var entry = (RosterEntry)AccessTools.Method(typeof(RosterManager), "GetRosterEntryByActorGuid").Invoke(roster, new object[] { guid });
            if (entry != null && entry.GetRosterStatus() != status) entry.SetRosterStatus(status, 0u);
        }

        // ---- fights ---------------------------------------------------------------------------------

        /// <summary>Starts a normal DD2 battle for the current party; control comes back to the hub afterwards.</summary>
        public static void StartBattle(string battleConfigurationId, string arenaScene)
        {
            if (!InHub) throw new InvalidOperationException("a battle can only start from the Estate hub mode");
            var party = Roster.GetActorGuids(RosterStatusType.PARTY);
            if (party.Count == 0) throw new InvalidOperationException("no party");
            var scenario = new CombatScenarioData(battleConfigurationId, arenaScene, EstateMode.DungeonFight, party);
            // (the fight's scenes are loaded once DD2's wipe has closed over the hub, not under it: EstateScenes)
            EstateScenes.BeginFight(scenario);
            Plugin.Log.LogInfo($"Estate battle: {battleConfigurationId} in {arenaScene}, party {string.Join(",", party)}");
        }

        public static object Describe()
        {
            var started = Singleton<GameTypeMgr>.Instance.IsGameTypeStarted;
            var heroes = new List<object>();
            if (started && Roster != null)
            {
                foreach (var guid in AllGuids(Roster))
                {
                    var actor = SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance.GetLibraryElement(guid);
                    heroes.Add(new
                    {
                        guid,
                        cls = actor?.ActorDataId,
                        hp = actor != null ? Mathf.RoundToInt(actor.HpRaw) : -1,
                        stress = actor != null ? actor.Stress : -1,
                        party = Roster.GetIsActorInParty(guid)
                    });
                }
            }
            return new
            {
                active = Active,
                mode = GameModeMgr.CurrentMode?.GetName(),
                changing = Singleton<GameModeMgr>.Instance.IsChangingState(),
                gameType = Singleton<GameTypeMgr>.Instance.CurrentGameType?.GetName(),
                gameTypeStarted = started,
                heroes
            };
        }
    }
}
