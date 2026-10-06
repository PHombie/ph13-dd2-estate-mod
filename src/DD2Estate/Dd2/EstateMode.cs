using System.Linq;
using System.Reflection;
using Assets.Code.Combat;
using Assets.Code.Game;
using Assets.Code.UI.Transitions;

namespace DD2Estate.Dd2
{
    /// <summary>
    /// The mod's own entries in DD2's class-enums. Both enums have private constructors, so the instances are
    /// made by reflection once at start-up; they register themselves by name like the vanilla ones.
    /// </summary>
    internal static class EstateMode
    {
        /// <summary>Game mode for the hamlet and for dungeon exploration: no scene, no vanilla system claims it.</summary>
        public static GameModeType Hub { get; private set; }

        /// <summary>
        /// Combat source of dungeon fights: a party wipe does not end anything by itself, and none of DD2's
        /// loot tables is rolled for the fight (it pays DD1's battle loot through DD1's scroll into the bag:
        /// DungeonRun, Core/BattleLoot.cs).
        /// </summary>
        public static CombatSource DungeonFight { get; private set; }

        public static void Register()
        {
            // Touch a vanilla value first so every vanilla instance exists (and keeps its index) before ours.
            var vanillaMode = GameModeType.MAIN_MENU;
            var vanillaSource = CombatSource.UNSET;

            var modeCtor = typeof(GameModeType).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).First(c => c.GetParameters().Length == 18);
            Hub = (GameModeType)modeCtor.Invoke(new object[]
            {
                "ESTATE", 0x2000, SceneTransition.FADE_IN_AND_OUT, /*showTransitionThrobber*/ false, /*sceneName*/ "", /*inputMapName*/ "",
                /*isLoadedSceneSetMode*/ false, /*loadedSceneModePriority*/ 0, /*canTransitionToSelf*/ false, /*setSceneAsActive*/ false,
                /*isGameTypeStartPoint*/ true, /*isGameTypeEndPoint*/ false, /*isInBiome*/ false, /*isManualHandleGameOver*/ false,
                /*isRunEndCashOut*/ false, /*canAbandon*/ false, /*fillDebugRoster*/ false, /*isSceneLoadedAutomatically*/ false
            });

            var sourceCtor = typeof(CombatSource).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).First(c => c.GetParameters().Length == 7);
            DungeonFight = (CombatSource)sourceCtor.Invoke(new object[]
            {
                "estate_dungeon", /*canReturnToDriving*/ false, /*isEndOnPartyDead*/ false, /*checkForTestBattleConfiguration*/ false,
                /*useLootTables*/ false, /*useStoryLootTables*/ false, /*nodeType*/ null
            });

            Plugin.Log.LogInfo($"registered game mode {Hub.GetName()} (#{Hub.EnumValue}, after {vanillaMode.GetName()}) and combat source {DungeonFight.GetName()} (after {vanillaSource.GetName()})");
        }
    }
}
