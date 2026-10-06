using System.Collections.Generic;
using System.Linq;
using Assets.Code.Achievements;
using Assets.Code.Kingdom;
using Assets.Code.Platform;
using Assets.Code.Utils.Serialization;
using HarmonyLib;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Dd2
{
    /// <summary>Every kingdom.json written during an Estate session carries the mod's section.</summary>
    [HarmonyPatch(typeof(PlatformMgr), nameof(PlatformMgr.SaveRunJson))]
    internal static class EstateSectionIsSaved
    {
        private static void Prefix(JObject jsonObject, RunSaveFile saveFile)
        {
            if (EstateSession.Active && saveFile == RunSaveFile.KINGDOM) EstatePersistence.AddSection(jsonObject);
        }
    }

    /// <summary>The game reads kingdom.json once per load; the mod's section is taken from that same read.</summary>
    [HarmonyPatch(typeof(PlatformMgr), nameof(PlatformMgr.TryLoadRunJson))]
    internal static class EstateSectionIsLoaded
    {
        private static void Postfix(RunSaveFile saveFile, ref JObject runJson, bool __result)
        {
            if (__result && saveFile == RunSaveFile.KINGDOM) EstatePersistence.ReadSection(runJson);
        }
    }

    /// <summary>
    /// A kingdom ends by day limit, lost inns, an empty roster or a dead gang boss; the profile then records the
    /// ending and the slot is on its way to being deleted. An Estate has no such ending.
    /// </summary>
    [HarmonyPatch(typeof(KingdomManager), "SetGameOver")]
    internal static class EstateNeverEndsAsKingdom
    {
        private static bool Prefix() => !EstateSession.Active;
    }

    /// <summary>
    /// The Estate slot is a Kingdom-format save in the Estate profile. Continued from the Kingdoms menu it would
    /// resume in an inn that does not exist, so it is left out of that list.
    /// </summary>
    [HarmonyPatch(typeof(KingdomSaveSelectUIBhv), nameof(KingdomSaveSelectUIBhv.GenerateKingdomSaveStats))]
    internal static class EstateSlotIsNotAKingdom
    {
        private static void Postfix(Dictionary<string, KingdomSaveStats> ___m_KingdomSaveStats)
        {
            foreach (var slot in ___m_KingdomSaveStats.Keys.Where(EstatePersistence.IsEstateSlot).ToList())
                ___m_KingdomSaveStats.Remove(slot);
        }
    }

    /// <summary>
    /// Every unlock goes through this one call: it marks the profile, raises the toast and queues the Steam
    /// unlock. Skipping it keeps Estate play out of all three.
    /// </summary>
    [HarmonyPatch(typeof(AchievementsMgr), nameof(AchievementsMgr.QueueAchievementUnlock))]
    internal static class NoAchievementsFromEstate
    {
        private static bool Prefix() => !EstatePersistence.BlocksAchievements;
    }
}
