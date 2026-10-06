using Assets.Code.UI.Managers;
using HarmonyLib;

namespace DD2Estate.Dd2
{
    // One inventory (docs/recon/inventory-unification.md, 4.4): what the estate owns is kept by the estate and
    // shown by the estate's own screens. DD2's screens over the same things are shut while an Estate session
    // runs. Each patch fails on its own.

    /// <summary>
    /// DD2's player inventory screen never opens in the Estate. Every way into it asks this question first
    /// (TryShowPlayerInventory, TogglePlayerInventory, ShowInnPlayerInventory, ShowHospital): a click on a
    /// trinket or item slot of the character sheet, the Inventory key on the results scene. The screen showed
    /// a second view of the estate's stores, where anything could be thrown away.
    /// </summary>
    [HarmonyPatch(typeof(CommonUiBhv), nameof(CommonUiBhv.GetCanShowPlayerInventory))]
    internal static class Dd2InventoryScreenStaysShut
    {
        private static void Postfix(ref bool __result)
        {
            if (EstateSession.Active) __result = false;
        }
    }

    /// <summary>
    /// DD2's character sheet stays as the hero sheet (stats, skills, quirks, the skill loadout), but what a
    /// hero wears and carries is not changed by DD2's own widgets there: its "Trinkets" give way to DD1's
    /// "Equipment" (Estate/SheetEquipment.cs: weapon, armour and the two trinket slots, drawn by the mod), which
    /// works with the Trinket Inventory in town; in a dungeon trinkets change on the raid panel. The mod's own
    /// calls ask for that already; this catches DD2's own keys (the results scene, the loot window's buttons).
    /// </summary>
    [HarmonyPatch(typeof(CommonUiBhv), nameof(CommonUiBhv.ShowCharacterSheet))]
    internal static class Dd2SheetDoesNotEditInventory
    {
        private static void Prefix(ref bool isInventoryEditable, ref bool autoselectTrinketSlot)
        {
            if (!EstateSession.Active) return;
            isInventoryEditable = false;
            autoselectTrinketSlot = false;
        }
    }
}
