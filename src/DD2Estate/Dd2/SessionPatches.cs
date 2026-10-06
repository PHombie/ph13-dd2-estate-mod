using Assets.Code.Combat.Events;
using Assets.Code.Game;
using Assets.Code.Kingdom;
using Assets.Code.Utils.Serialization;
using HarmonyLib;

namespace DD2Estate.Dd2
{
    /// <summary>
    /// While an Estate session is active the vanilla code still tries to go back to the road, an inn or the
    /// embark screen after fights and wipes. All of those land in the Estate hub instead. So does the results
    /// scene DD2 shows between a fight and the road (the party before its stagecoach): a fight of an expedition
    /// ends in the corridor it was fought in (<see cref="EstateScenes"/>).
    /// </summary>
    [HarmonyPatch(typeof(GameModeMgr), nameof(GameModeMgr.SetMode))]
    internal static class RedirectVanillaHubModes
    {
        private static void Prefix(ref GameModeType mode, ref Assets.Code.UI.Transitions.SceneTransition transitionOverride)
        {
            if (!EstateSession.Active) return;
            if (mode == GameModeType.RESULTS && EstateScenes.OnResultsAsked(ref transitionOverride))
            {
                mode = EstateMode.Hub;
                return;
            }
            // The way to the main menu is the game's own menu (the estate bar's candles open it): the estate is
            // written before it is left, whichever button asked.
            if (mode == GameModeType.MAIN_MENU && EstateSession.InHub) EstatePersistence.SaveNow("exit");
            if (mode == GameModeType.DRIVING || mode == GameModeType.INN || mode == GameModeType.EMBARK ||
                mode == GameModeType.HERO_SELECT || mode == GameModeType.ALTAR_OF_HOPE)
            {
                Plugin.Log.LogInfo($"Estate: redirecting mode {mode.GetName()} -> {EstateMode.Hub.GetName()}");
                mode = EstateMode.Hub;
            }
            // (a fight left for the hub without its results: a lost party)
            if (mode == EstateMode.Hub && GameModeMgr.CurrentMode == GameModeType.COMBAT) EstateScenes.OnFightLeftForHub();
        }
    }

    /// <summary>Kingdoms sends a wiped party back to an inn and may end the kingdom; the Estate decides that itself.</summary>
    [HarmonyPatch(typeof(KingdomBhv), "HandleEventBattleResult")]
    internal static class KingdomIgnoresEstateBattles
    {
        private static bool Prefix() => !EstateSession.Active;
    }

    /// <summary>Vanilla save points assume a road, an inn or a fight to resume in; only the mod's own hub snapshots are written.</summary>
    [HarmonyPatch(typeof(SaveUtils), nameof(SaveUtils.SaveCurrentGameMode))]
    internal static class NoVanillaSnapshotsInEstate
    {
        private static bool Prefix() => !EstateSession.Active || EstatePersistence.Saving;
    }
}

namespace DD2Estate.Dd2
{
    /// <summary>
    /// A fight of the Kingdom game type shows, beside the retreat button, the icon of the gang that threatens the
    /// kingdom with its escalation level (BattleInfoUiBhv.InitUI). The Estate has no gangs and no escalation of
    /// that kind: its fights have no such icon.
    /// </summary>
    [HarmonyLib.HarmonyPatch(typeof(Assets.Code.UI.BattleInfoUiBhv), nameof(Assets.Code.UI.BattleInfoUiBhv.InitUI))]
    internal static class EstateFightsShowNoGangIcon
    {
        private static readonly System.Reflection.FieldInfo Gang = HarmonyLib.AccessTools.Field(typeof(Assets.Code.UI.BattleInfoUiBhv), "m_gangEscalationBhv");

        private static void Postfix(Assets.Code.UI.BattleInfoUiBhv __instance)
        {
            if (!EstateSession.Active) return;
            var gang = Gang?.GetValue(__instance) as UnityEngine.Component;
            if (gang != null) gang.gameObject.SetActive(false);
        }
    }
}

namespace DD2Estate.Dd2
{
    /// <summary>
    /// The game's menu leaves to the desktop without a word to anyone (Application.Quit). From the Estate's hub the
    /// estate is written first; on the way to the main menu RedirectVanillaHubModes does the same.
    /// </summary>
    [HarmonyLib.HarmonyPatch(typeof(Assets.Code.UI.Controllers.PauseMenuUiControllerBhv), "ExitApplication")]
    internal static class EstateIsWrittenBeforeTheGameQuits
    {
        private static void Prefix()
        {
            if (EstateSession.InHub) EstatePersistence.SaveNow("quit");
        }
    }
}
