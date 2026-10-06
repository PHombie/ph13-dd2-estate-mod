using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Assets.Code.Actor.Events;
using Assets.Code.Combat.Events;
using Assets.Code.Game;
using Assets.Code.Kingdom;
using Assets.Code.Roster;
using Assets.Code.Utils;
using DD2Estate.Estate;
using HarmonyLib;

namespace DD2Estate.Dd2
{
    /// <summary>
    /// A new Kingdom-type game fills its roster with one hero of every class the moment it starts
    /// (RosterManager.HandleEventGameTypeStarted → FillRoster). An estate starts with four heroes and recruits
    /// the others at the stage coach, so the game adds nobody by itself while an Estate session runs.
    /// </summary>
    [HarmonyPatch(typeof(RosterManager), "AddMissingHeroesToRoster")]
    internal static class EstateRosterIsNotAutoFilled
    {
        private static bool Prefix(ref IReadOnlyList<uint> __result)
        {
            if (!EstateSession.Active) return true;
            __result = new List<uint>();
            return false;
        }
    }

    /// <summary>
    /// The roster drops every entry whose class appears again later in the list (RemoveInvalidRosterEntries, run
    /// on every load): one hero per class. An estate holds up to four of a class, one per path, so while an
    /// Estate session runs two entries of a class are not a fault. The method's only string comparison is that
    /// test; the rest of the validation (missing actor, broken class data) runs as it is.
    /// </summary>
    [HarmonyPatch(typeof(RosterManager), nameof(RosterManager.RemoveInvalidRosterEntries))]
    internal static class EstateKeepsHeroesOfOneClass
    {
        private static readonly MethodInfo StringEquals = AccessTools.Method(typeof(string), "op_Equality", new[] { typeof(string), typeof(string) });
        private static readonly MethodInfo Replacement = AccessTools.Method(typeof(EstateKeepsHeroesOfOneClass), nameof(IsDuplicateClass));

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var replaced = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(StringEquals))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = Replacement;
                    replaced++;
                }
                yield return instruction;
            }
            if (replaced != 1) Plugin.Log.LogError("Estate roster: the game's one-hero-per-class test was found " + replaced + " times; heroes sharing a class may not survive a load");
        }

        public static bool IsDuplicateClass(string classId, string otherClassId)
        {
            return !EstateSession.Active && classId == otherClassId;
        }
    }

    /// <summary>
    /// The game's rich presence (the "in combat against ..." line of Steam and Discord) takes a fight's end from
    /// the modes that follow a fight in the game. The Estate's hub is not one of them, so after an Estate fight
    /// it still believes a fight is on, and at the next hero death outside one (hunger, a trap, a curio) it
    /// reads the fight's scenario, which is gone. That throws inside the death event, and a listener's
    /// exception ends the event for every later listener: the roster never hears of the death. With no
    /// scenario there is no fight to describe, so the handler is skipped.
    /// </summary>
    [HarmonyPatch(typeof(Assets.Code.Platform.RichPresenceMgr), "OnActorDeath")]
    internal static class RichPresenceKnowsNoFightIsOn
    {
        private static bool Prefix()
        {
            return !EstateSession.Active || Singleton<GameTypeMgr>.Instance.CombatScenarioData != null;
        }
    }

    /// <summary>
    /// The roster's own death handler is the last listener of a death: the hero is already a corpse, but the
    /// actor still exists and its roster entry is not marked dead yet. The graveyard takes its record here;
    /// the handler itself runs unchanged (entry → DEAD), so the fight goes on exactly as in a kingdom.
    /// </summary>
    [HarmonyPatch(typeof(RosterManager), "HandleEventActorDeath")]
    internal static class EstateRemembersTheFallen
    {
        private static void Prefix(RosterManager __instance, EventActorDeath __0)
        {
            RosterLifecycle.OnActorDeath(__instance, __0);
        }
    }

    /// <summary>
    /// With the Kingdom's default difficulty a dead hero stays in the roster for ever as a DEAD entry (no
    /// respawn, no refill) and the class is never offered again. On the way back into the hub, right after the
    /// game has dropped the dead actors, the estate drops those entries too: the hero is gone, the class is free.
    /// </summary>
    [HarmonyPatch(typeof(GameTypeMgr), nameof(GameTypeMgr.OnGameModeExitComplete))]
    internal static class EstateBuriesItsDead
    {
        private static void Postfix()
        {
            if (EstateSession.Active && Singleton<GameModeMgr>.Instance.GetNextMode() == EstateMode.Hub) RosterLifecycle.BuryDead();
        }
    }

    /// <summary>
    /// A wiped party: in Kingdoms KingdomBhv.HandleEventBattleResult ends the kingdom or sends the survivors'
    /// stagecoach to an inn, and nothing else leaves the fight (the combat presentation stops at "party dead"
    /// when there is no run score). KingdomIgnoresEstateBattles switches that handler off for the Estate; this
    /// postfix still runs and sets the way back to the hub. The estate itself goes on with whoever is left.
    /// </summary>
    [HarmonyPatch(typeof(KingdomBhv), "HandleEventBattleResult")]
    internal static class EstateSurvivesALostParty
    {
        private static void Postfix(EventBattleResult __0)
        {
            RosterLifecycle.OnBattleResult(__0);
        }
    }
}
