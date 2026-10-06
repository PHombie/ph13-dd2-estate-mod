using Assets.Code.Actor;
using Assets.Code.Boss;
using Assets.Code.Combat;
using Assets.Code.Combat.BattleModifier;
using Assets.Code.Condition;
using HarmonyLib;

namespace DD2Estate.Dd2
{
    // The four places where the tier of an estate fight meets the game (see Difficulty). Each patch does
    // nothing unless Difficulty has a plan for the fight in progress, and each fails on its own: a patch the
    // game no longer accepts costs one of the four, not the others.

    /// <summary>
    /// Every actor joins its team through here: at the start of a battle, as a later wave and as a summon.
    /// The enemies get the tier's stats container under their battle data (cleared by the game in
    /// ActorInstance.OnRemovedFromTeam and OnBattleEnd).
    /// </summary>
    [HarmonyPatch(typeof(ActorInstance), nameof(ActorInstance.OnAddedToTeam))]
    internal static class EnemiesTakeTheTierStats
    {
        private static void Postfix(ActorInstance __instance, Team team)
        {
            if (team == null || team.m_TeamIndex != Difficulty.EnemyTeam) return;
            Difficulty.Fit(__instance);
        }
    }

    /// <summary>
    /// The game ordains from the boss of the running expedition (BossCalculation.RollBossModifier); a Kingdom
    /// has no expedition, so nothing is ever ordained there. The estate's tier names the row instead.
    /// </summary>
    [HarmonyPatch(typeof(BossCalculation), nameof(BossCalculation.RollBossModifier))]
    internal static class TierOrdainsMonsters
    {
        private static void Postfix(ActorDataClass actorDataClass, ref BossModifierDefinition __result)
        {
            if (__result == null) __result = Difficulty.RollOrdain(actorDataClass);
        }
    }

    /// <summary>A gang boss of a veteran or champion quest fights with its gang's own modifier for that escalation.</summary>
    [HarmonyPatch(typeof(BattleModifierCalculation), nameof(BattleModifierCalculation.RollBattleModifier))]
    internal static class TierForcesTheGangModifier
    {
        private static void Postfix(ref BattleModifierDefinition __result)
        {
            var forced = Difficulty.ForcedBattleModifier();
            if (forced != null) __result = forced;
        }
    }

    /// <summary>
    /// Conditions on the state of the game (ConditionCalculation.GetGameValueForConditionType: run values, the
    /// act boss, ...) are what loot tables, fight tables and monster skills ask to tell an easy Kingdom from a
    /// hard one. During an estate fight the escalation they see is the tier's, and an act boss sees its own act.
    /// </summary>
    [HarmonyPatch(typeof(ConditionCalculation), "GetGameValueForConditionType")]
    internal static class TierAnswersGameConditions
    {
        private static void Postfix(ConditionType conditionType, string conditionString, ref float __result)
        {
            Difficulty.Answer(conditionType, conditionString, ref __result);
        }
    }
}
