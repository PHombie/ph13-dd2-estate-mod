using Assets.Code.UI;
using DD2Estate.Dd2;
using HarmonyLib;
using UnityEngine.EventSystems;

namespace DD2Estate.Estate
{
    /// <summary>
    /// A click on a combat skill of the character sheet puts it on or off in the Estate too. DD2 passes the click
    /// on only on the road, at an inn and at the crossroads (CharacterSheetSkillButtonBhv.OnPointerClick asks for
    /// its modes DRIVING, INN and HERO_SELECT); the Estate's hamlet and its dungeons are none of them, so a hero's
    /// five skills could not be chosen at all (the owner, 2026-10-06: "heroes' skills cannot be switched on
    /// and off now"). The rest of DD2's rules stand, they are in OnClick: nothing changes during a fight,
    /// nothing on a sheet that was opened for looking only, and no more skills are on than the game allows.
    /// DD1 lets a hero's skills be changed anywhere outside a fight, the hamlet and the dungeon alike.
    /// </summary>
    [HarmonyPatch(typeof(CharacterSheetSkillButtonBhv), nameof(CharacterSheetSkillButtonBhv.OnPointerClick))]
    internal static class SheetSkillsAreChosenInTheEstate
    {
        private static bool Prefix(CharacterSheetSkillButtonBhv __instance, PointerEventData eventData)
        {
            if (!EstateSession.Active || __instance == null) return true;
            __instance.OnClick(false);
            return false;
        }
    }
}
