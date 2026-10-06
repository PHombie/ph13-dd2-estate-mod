using System;
using System.Collections.Generic;
using System.Reflection;
using Assets.Code.Item;
using Assets.Code.Loot;
using Assets.Code.Utils;
using HarmonyLib;

namespace DD2Estate.Dd2
{
    /// <summary>
    /// What DD2 would hand out by itself during an Estate session, and what becomes of it
    /// (docs/recon/inventory-unification.md, 4.3): nothing is granted that the estate has no ledger for.
    ///
    /// - Loot: DD2's loot manager takes none (the patch on LootManager.AddLoot). A fight's own tables are off
    ///   at the combat source (EstateMode); what still arrives there is the death loot of the monsters, a
    ///   hero's run goal, an effect that drops something: relics, materials, "mastery points", flame points.
    ///   With nothing to show, DD2's loot window never opens and its results scene passes by itself. What was
    ///   turned away is kept here in words, for the dev bridge.
    /// - A dead hero's gear: DD2 empties the trinket and combat item slots of a hero who dies into the list
    ///   its next loot window shows. The trinkets are kept here instead until the expedition claims them (DD1
    ///   offers them as loot at once, on its "hero death" scroll: DungeonRun), or, with no expedition on,
    ///   until the hub's sweep puts them in the estate's stores.
    /// </summary>
    internal static class Dd2Loot
    {
        private const int Remembered = 40;
        private static readonly List<string> Skipped = new List<string>();
        private static readonly List<string> Fallen = new List<string>();
        private static readonly FieldInfo NextWindow = AccessTools.Field(typeof(LootManager), "m_NextWindowItems");

        /// <summary>How often DD2 tried to grant loot since the game started.</summary>
        public static int SkippedCalls { get; private set; }

        /// <summary>The last things DD2 tried to grant, newest last.</summary>
        public static IReadOnlyList<string> SkippedLoot => Skipped;

        /// <summary>Trinkets of heroes who died and that nobody has claimed yet (DD2 item ids).</summary>
        public static IReadOnlyList<string> FallenTrinkets => Fallen;

        /// <summary>DD2 wanted to grant this: it is written down and dropped.</summary>
        public static void Skip(IReadOnlyList<Reward<LootType>> rewards, LootReason reason)
        {
            SkippedCalls++;
            if (rewards == null || rewards.Count == 0) return;
            var parts = new List<string>();
            foreach (var reward in rewards)
                if (reward != null) parts.Add((reward.m_type != null ? reward.m_type.GetName() : "?") + ":" + reward.m_id + " x" + reward.m_qty);
            var line = (reason != null ? reason.GetName() : "?") + ": " + string.Join(", ", parts);
            Skipped.Add(line);
            if (Skipped.Count > Remembered) Skipped.RemoveAt(0);
            Plugin.Log.LogInfo("Estate: DD2's loot is not taken (" + line + ")");
        }

        /// <summary>The slots of a hero who died are emptied: the trinkets wait here, anything else is gone.</summary>
        public static void TakeFromTheFallen(ItemInventory inventory)
        {
            if (inventory == null) return;
            var items = new List<ItemInstance>();
            try { inventory.PopAllItemsInto(items); }
            catch (Exception e)
            {
                Plugin.Log.LogError("Estate: a fallen hero's slots could not be emptied: " + e);
                return;
            }
            Keep(items, "a fallen hero");
        }

        /// <summary>
        /// What lies in the list DD2's next loot window would show (a save from before the estate kept its own
        /// books can carry a dead party's trinkets there). Returns what was taken out, in words;
        /// <paramref name="dry"/> only looks.
        /// </summary>
        public static List<string> TakeNextWindow(LootManager manager, bool dry)
        {
            var found = new List<string>();
            if (manager == null) return found;
            foreach (var item in manager.NextWindowItems)
                if (ItemUtils.IsValid(item)) found.Add(item.GetItemDefinition().m_id + " x" + item.GetQty());
            if (dry || found.Count == 0) return found;
            if (!(NextWindow?.GetValue(manager) is List<ItemInstance> list))
            {
                Plugin.Log.LogWarning("Estate: the list of DD2's next loot window could not be reached; " + found.Count + " item(s) stay in it");
                return new List<string>();
            }
            var items = new List<ItemInstance>(list);
            list.Clear();
            Keep(items, "DD2's next loot window");
            return found;
        }

        private static void Keep(List<ItemInstance> items, string from)
        {
            var dropped = new List<string>();
            foreach (var item in items)
            {
                if (!ItemUtils.IsValid(item)) continue;
                var definition = item.GetItemDefinition();
                if (definition.m_type == ItemType.TRINKET)
                    for (var i = 0; i < Math.Max(1, item.GetQty()); i++) Fallen.Add(definition.m_id);
                else dropped.Add(definition.m_id + " x" + item.GetQty());
            }
            if (dropped.Count > 0) Plugin.Log.LogInfo("Estate: out of " + from + ", with no place in the estate: " + string.Join(", ", dropped));
        }

        /// <summary>Hands over the trinkets of the fallen and forgets them.</summary>
        public static List<string> ClaimFallen()
        {
            var claimed = new List<string>(Fallen);
            Fallen.Clear();
            return claimed;
        }

        /// <summary>The session is over: nothing waits for the next one.</summary>
        public static void Clear()
        {
            Fallen.Clear();
        }
    }

    /// <summary>
    /// DD2's loot manager takes nothing while an Estate session runs: a fight pays DD1's battle loot into the
    /// bag, and the estate has no place for anything else DD2 hands out.
    /// </summary>
    [HarmonyPatch(typeof(LootManager), nameof(LootManager.AddLoot))]
    internal static class EstateTakesNoDd2Loot
    {
        private static bool Prefix(IReadOnlyList<Reward<LootType>> lootRewards, LootReason reason, ref bool __result)
        {
            if (!EstateSession.Active) return true;
            Dd2Loot.Skip(lootRewards, reason);
            __result = false;
            return false;
        }
    }

    /// <summary>
    /// A hero who dies has the trinket and combat item slots emptied into the list of DD2's next loot window
    /// (ActorInstance.HandleEventActorDeathLogic). In the Estate the trinkets go to the mod instead.
    /// </summary>
    [HarmonyPatch(typeof(LootManager), nameof(LootManager.EmptyInventoryIntoNextWindowLoot))]
    internal static class TheFallenLeaveTheirGearToTheEstate
    {
        private static bool Prefix(ItemInventory inventory)
        {
            if (!EstateSession.Active) return true;
            Dd2Loot.TakeFromTheFallen(inventory);
            return false;
        }
    }
}
