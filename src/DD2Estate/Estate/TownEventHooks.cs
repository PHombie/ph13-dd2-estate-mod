using System;
using System.Collections.Generic;

namespace DD2Estate.Estate
{
    /// <summary>
    /// What the week's town event asks of the other systems of the estate. Each of them owns its prices and its
    /// slots, so the event does not reach into them: they ask here at the place where the number is made.
    /// Without an event every answer is the neutral one (not closed, factor 1, nothing free), so a call costs
    /// nothing and changes nothing in an ordinary week.
    ///
    ///   Tavern, Abbey        ActivityRules.Slots -> ActivityClosed; ActivityRules.Cost -> ActivityPrice;
    ///                        ActivityLedger.EndWeek -> ActivityReliefFactor
    ///   Sanitarium           SanitariumRules.Price -> ActivityPrice (ward id)
    ///   Provision shop       ProvisionPriceFactor, ProvisionStock (DD1 item type: "supply", "provision")
    ///   Blacksmith           FreeUpgrades("weapon" / "armour"), UseFreeUpgrade
    ///   Building upgrades    FreeUpgrades("building"), UseFreeUpgrade
    ///   Quest level caps     LevelRestrictionLifted
    ///
    /// docs/recon/town-events.md has the lines to put into each of those files. <see cref="Asked"/> counts the
    /// calls, so the dev bridge (events.state) shows which hooks are wired.
    /// </summary>
    internal static class TownEventHooks
    {
        /// <summary>How often each hook was asked since the game started.</summary>
        public static readonly Dictionary<string, int> Asked = new Dictionary<string, int>();

        private static void Note(string hook)
        {
            Asked.TryGetValue(hook, out var count);
            Asked[hook] = count + 1;
        }

        // ---- Tavern, Abbey, Sanitarium ---------------------------------------------------------------------

        /// <summary>DD1's activity_lock: the activity takes nobody this week ("Empty Kegs": the bar).</summary>
        public static bool ActivityClosed(string activityId)
        {
            Note("ActivityClosed");
            foreach (var effect in TownEvents.Effects(TownEvents.Current, "activity_lock"))
                if (effect.Text == activityId) return true;
            return false;
        }

        /// <summary>
        /// The share of its price an activity or a ward asks this week: 0 when DD1's free_activity names it,
        /// 1 + activity_cost_change (DD1 writes -0.5 for half price), otherwise 1.
        /// </summary>
        public static float ActivityPriceFactor(string activityId)
        {
            Note("ActivityPriceFactor");
            var active = TownEvents.Current;
            foreach (var effect in TownEvents.Effects(active, "free_activity"))
                if (effect.Text == activityId) return 0f;
            var factor = 1f;
            foreach (var effect in TownEvents.Effects(active, "activity_cost_change"))
                if (effect.Text == activityId) factor *= Math.Max(0f, 1f + (float)effect.Number);
            return factor;
        }

        /// <summary>An activity's or a ward's price after the week's event: 0 when free, otherwise at least 1, halves rounded up.</summary>
        public static int ActivityPrice(string activityId, int price)
        {
            if (price <= 0) return price;
            var factor = ActivityPriceFactor(activityId);
            if (factor <= 0f) return 0;
            return factor == 1f ? price : Math.Max(1, (int)Math.Floor(price * factor + 0.5f));
        }

        /// <summary>
        /// DD1's in_activity_buff, for the week that has just ended: the share of its usual stress relief an
        /// activity gives ("A Ray of Sunlight" 1.33, "The Miserable Dark" 0.67; the amounts are DD1's buffs
        /// town_event_in_activity_*). To be asked while the week advances, when the stays are settled: by then
        /// the week number has moved on, so this looks at the week before.
        /// </summary>
        public static float ActivityReliefFactor(string activityId)
        {
            Note("ActivityReliefFactor");
            var factor = 1f;
            var ended = TownEvents.EventOfWeek(EstateState.Current.Week - 1);
            foreach (var effect in TownEvents.Effects(ended, "in_activity_buff"))
            {
                var buff = TownEvents.Catalog.Buff(effect.Text);
                if (buff != null && buff.Stat == "stress_heal_received_percent" && buff.Rule == "in_activity" && buff.RuleText == activityId) factor += (float)buff.Amount;
            }
            return Math.Max(0f, factor);
        }

        // ---- provisions ------------------------------------------------------------------------------------

        /// <summary>
        /// DD1's provision_item_type_cost_change: the share of its price an item of this DD1 type ("supply",
        /// "provision") costs this week: 0.5 for "Supply Run", 0 for "Bumper Crop".
        /// </summary>
        public static float ProvisionPriceFactor(string itemType)
        {
            Note("ProvisionPriceFactor");
            var factor = 1f;
            foreach (var effect in TownEvents.Effects(TownEvents.Current, "provision_item_type_cost_change"))
                if (effect.Text == itemType) factor *= Math.Max(0f, 1f + (float)effect.Number);
            return factor;
        }

        /// <summary>
        /// DD1's provision_item_type_amount_change: what is left of a shelf this week ("Lost Shipment": half).
        /// Rounded up, so that a shelf of one keeps its one (the file gives the share, not the rounding).
        /// </summary>
        public static int ProvisionStock(string itemType, int stock)
        {
            Note("ProvisionStock");
            var factor = 1f;
            foreach (var effect in TownEvents.Effects(TownEvents.Current, "provision_item_type_amount_change"))
                if (effect.Text == itemType) factor *= Math.Max(0f, 1f + (float)effect.Number);
            return factor == 1f ? stock : Math.Max(0, (int)Math.Ceiling(stock * factor - 0.0001f));
        }

        // ---- free upgrades ---------------------------------------------------------------------------------

        /// <summary>
        /// DD1's upgrade_tag_free: purchases of this kind that are still free this week. Tags: "weapon" and
        /// "armour" (one level of a hero's gear at the blacksmith), "building" (one step of a building's tree).
        /// </summary>
        public static int FreeUpgrades(string tag)
        {
            Note("FreeUpgrades");
            var active = TownEvents.Current;
            return active != null && active.FreeUpgrades.TryGetValue(tag, out var left) ? Math.Max(0, left) : 0;
        }

        /// <summary>Spends one free purchase of this kind; false when there is none (then the price is due).</summary>
        public static bool UseFreeUpgrade(string tag)
        {
            Note("UseFreeUpgrade");
            var active = TownEvents.Current;
            if (active == null || !active.FreeUpgrades.TryGetValue(tag, out var left) || left <= 0) return false;
            active.FreeUpgrades[tag] = left - 1;
            Plugin.Log.LogInfo("Town events: a free " + tag + " upgrade is used (" + (left - 1) + " left)");
            TownEvents.RaiseChanged();
            return true;
        }

        // ---- quests ----------------------------------------------------------------------------------------

        /// <summary>DD1's remove_quest_hero_level_restriction ("Helping Hand"): seasoned heroes take any quest this week.</summary>
        public static bool LevelRestrictionLifted
        {
            get
            {
                Note("LevelRestrictionLifted");
                foreach (var effect in TownEvents.Effects(TownEvents.Current, "remove_quest_hero_level_restriction")) return effect != null;
                return false;
            }
        }
    }
}
