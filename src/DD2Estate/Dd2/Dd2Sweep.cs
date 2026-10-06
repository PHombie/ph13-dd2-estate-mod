using System;
using System.Collections.Generic;
using System.Linq;
using Assets.Code.Game;
using Assets.Code.Item;
using Assets.Code.Rules;
using Assets.Code.Run;
using Assets.Code.Utils;
using DD2Estate.Core;
using DD2Estate.Dungeon;
using DD2Estate.Estate;

namespace DD2Estate.Dd2
{
    /// <summary>
    /// DD2's player inventory is a transit box (docs/recon/inventory-unification.md, 4.1): the estate keeps
    /// nothing there, and whatever DD2's own code drops into it is moved to the estate's ledger for that thing
    /// the next time the hub is in, or removed when the estate has no ledger for it:
    ///
    /// - gold becomes the purse's, at what DD2's gold is worth in DD1's (<see cref="EstateState.Dd2GoldWorth"/>);
    /// - a trinket (one DD2 took off a hero who may no longer wear it: TrinketItemInventory.UpdateEquipped) lies
    ///   in the estate's stores, or, on an expedition, in the bag the hero could have put it in (in the stores
    ///   when the bag is full: nothing is lost to it);
    /// - a DD2 combat item: one that is the twin of a DD1 supply (bandages, antivenom ...) joins the bag as that
    ///   supply when an expedition is on; otherwise, and for every other combat item, the owner's choice
    ///   <see cref="PayForStrayCombatItems"/> says whether it is paid for or simply gone;
    /// - everything else (inn materials, baubles, inn items, stagecoach items, trophies, deliverables) is
    ///   removed, and DD2's hero upgrade points are set to nought: the estate has no use and no place for them.
    ///
    /// The same pass runs once after every load, and is then the migration of an older save: one from before
    /// the stores were the estate's own has its unworn trinkets (and, older still, its gold) in DD2's inventory,
    /// with whatever DD2's loot windows paid beside them. After a load the heroes' combat item slots are
    /// emptied too (the hub's pass leaves those alone: a fight is where they are filled). What the load's pass
    /// did is written into the Activity Log in one line. It changes memory only; the file changes with the next
    /// save.
    /// </summary>
    internal static class Dd2Sweep
    {
        /// <summary>
        /// Dev (ledger.hold): nothing is swept, neither after a load nor in the hub, so that what a sweep would
        /// do to a save can be looked at first (ledger.migrate with "dry") and then done by hand.
        /// </summary>
        public static bool Hold;

        /// <summary>
        /// OWNER'S CHOICE Q7 (docs/recon/inventory-unification.md 4.6): DD2 combat items found lying in the
        /// estate (an older save's, or one DD2 handed back) are paid for at DD1's sell value of a supply (true),
        /// or removed without pay (false). Everything else of DD2's is removed either way.
        /// </summary>
        public static bool PayForStrayCombatItems = true;

        /// <summary>What a pass moved, or would move.</summary>
        public sealed class Report
        {
            /// <summary>Gold into the purse for what lay in DD2's inventory (DD2's gold at its worth in DD1's).</summary>
            public int Gold;
            /// <summary>Trinkets out of DD2's inventory into the estate's stores (DD2 item ids).</summary>
            public readonly List<string> Trinkets = new List<string>();
            /// <summary>Trinkets out of DD2's inventory into the expedition's bag: DD2 took them off a hero of the party.</summary>
            public readonly List<string> ToBag = new List<string>();
            /// <summary>DD2 combat items that joined the expedition's bag as the DD1 supply they are the twin of ("bandages x2 as bandage").</summary>
            public readonly List<string> Supplies = new List<string>();
            /// <summary>DD2 combat items paid for, with the gold for each ("smoke_bomb x1: 5").</summary>
            public readonly List<string> Paid = new List<string>();
            /// <summary>What they came to as it went into the purse.</summary>
            public int PaidGold;
            /// <summary>What was removed for good: DD2 things the estate has no ledger for.</summary>
            public readonly List<string> Removed = new List<string>();
            /// <summary>DD2's hero upgrade points that were set to nought.</summary>
            public float HeroPoints;
            /// <summary>What was waiting for DD2's next loot window and was taken out of its list (a save can carry it).</summary>
            public readonly List<string> LootWindow = new List<string>();

            public bool Any => Gold > 0 || Trinkets.Count > 0 || ToBag.Count > 0 || Supplies.Count > 0 || Paid.Count > 0 || Removed.Count > 0 || HeroPoints > 0f || LootWindow.Count > 0;

            public override string ToString()
            {
                var parts = new List<string>();
                if (Gold > 0) parts.Add(Gold + " gold into the purse");
                if (Trinkets.Count > 0) parts.Add(Trinkets.Count + " trinket(s) into the stores (" + string.Join(", ", Trinkets) + ")");
                if (ToBag.Count > 0) parts.Add(ToBag.Count + " trinket(s) into the expedition's bag (" + string.Join(", ", ToBag) + ")");
                if (Supplies.Count > 0) parts.Add("into the expedition's bag as supplies: " + string.Join(", ", Supplies));
                if (Paid.Count > 0) parts.Add("combat items " + (PayForStrayCombatItems ? "paid at DD1's sell value (" + PaidGold + " gold)" : "removed without pay") + ": " + string.Join(", ", Paid));
                if (Removed.Count > 0) parts.Add("removed: " + string.Join(", ", Removed));
                if (HeroPoints > 0f) parts.Add(HeroPoints + " hero upgrade point(s) set to nought");
                if (LootWindow.Count > 0) parts.Add("out of the list of DD2's next loot window: " + string.Join(", ", LootWindow));
                return parts.Count > 0 ? string.Join("; ", parts) : "nothing";
            }

            /// <summary>The one line for the Activity Log, in the player's words.</summary>
            public string Told()
            {
                var kept = new List<string>();
                if (Gold > 0) kept.Add(Gold + " gold");
                if (Trinkets.Count + ToBag.Count > 0) kept.Add(Count(Trinkets.Count + ToBag.Count, "trinket"));
                if (Supplies.Count > 0) kept.Add("supplies for the expedition's bag");
                var text = kept.Count > 0 ? "The estate's books are one now: " + string.Join(", ", kept) + " taken over from the old stores." : "The estate's books are one now.";
                if (PaidGold > 0) text += " " + PaidGold + " gold paid for " + Count(Paid.Count, "kind") + " of fighting supplies the estate does not keep.";
                else if (Paid.Count > 0) text += " " + Count(Paid.Count, "kind") + " of fighting supplies the estate does not keep " + (Paid.Count == 1 ? "is" : "are") + " gone.";
                var gone = Removed.Count + (HeroPoints > 0f ? 1 : 0);
                if (gone > 0) text += " Put aside for good, having no use here: " + string.Join(", ", Removed.Concat(HeroPoints > 0f ? new[] { HeroPoints + " mastery point(s)" } : new string[0])) + ".";
                return text;
            }

            private static string Count(int n, string noun) => n + " " + noun + (n == 1 ? "" : "s");
        }

        /// <summary>What the pass after the last load moved; null before any load of this session.</summary>
        public static Report LastLoad { get; private set; }

        /// <summary>The format the save loaded last was written in (0: a new estate, nothing was loaded).</summary>
        public static int LoadedFormat { get; private set; }

        // DD2's inventory has been emptied once since the estate was entered. Only then is a trinket found there
        // one that DD2 has just taken off a hero; before, in an older save, it holds the estate's own stores.
        private static bool _emptied;

        /// <summary>A new estate: nothing was loaded, nothing was brought over.</summary>
        public static void NewEstate()
        {
            LoadedFormat = 0;
            LastLoad = null;
            _emptied = true;
        }

        /// <summary>The pass after a load (<see cref="EstateState.LoadFrom"/>): the migration of an older save.</summary>
        public static void AfterLoad(int savedFormat)
        {
            LoadedFormat = savedFormat;
            LastLoad = null;
            _emptied = false;
            if (Hold)
            {
                Plugin.Log.LogWarning("Estate: the sweep after the load is held back (ledger.hold); DD2's inventory is as the save had it");
                return;
            }
            LastLoad = Migrate(false);
        }

        /// <summary>
        /// The whole pass, the heroes' combat item slots included: what loading a save does, and what
        /// ledger.migrate does by hand when the load's pass was held back. Not dry and something moved: one
        /// line in the Activity Log.
        /// </summary>
        public static Report Migrate(bool dry)
        {
            var report = Run(dry, heroSlots: true);
            if (dry) return report;
            Plugin.Log.LogInfo("Estate: a save of format " + LoadedFormat + " brought over; out of DD2's keeping: " + report);
            if (report.Any)
            {
                try { ActivityLog.Add(ActivityLog.Kinds.Note, report.Told()); }
                catch (Exception e) { Plugin.Log.LogWarning("Estate: the Activity Log did not take the line about the old stores: " + e.Message); }
            }
            return report;
        }

        /// <summary>The hub's pass; quiet unless something was found.</summary>
        public static void InHub()
        {
            if (Hold) return;
            var report = Run(false);
            if (report.Any) Plugin.Log.LogInfo("Estate: out of DD2's inventory: " + report);
            // on an expedition, what the fallen wore is the party's to be offered
            DungeonRun.Current?.Tend();
        }

        /// <summary>
        /// One pass. <paramref name="dry"/>: nothing is moved, the report says what would be.
        /// <paramref name="heroSlots"/>: the combat item slots of the estate's heroes are emptied as well.
        /// </summary>
        public static Report Run(bool dry, bool heroSlots = false)
        {
            var report = new Report();
            var game = Singleton<GameTypeMgr>.Instance;
            var inventory = game.PlayerInventory;
            if (inventory == null) return report;
            try
            {
                var gold = RulesManager.GetRules<InventoryRules>().GOLD;
                report.Gold = dry ? inventory.GetGoldQty() * EstateState.Dd2GoldWorth : EstateState.TakeDd2Gold();

                var run = DungeonRun.Current;
                var trinkets = new List<ItemDefinition>();
                var combat = new Dictionary<ItemDefinition, int>();
                var others = new List<ItemDefinition>();
                foreach (var item in inventory.GetValidItems())
                {
                    var definition = item.GetItemDefinition();
                    // the gold is counted above (and gone by now, unless this is a dry run)
                    if (definition == null || definition == gold) continue;
                    if (definition.m_type == ItemType.TRINKET)
                    {
                        if (!trinkets.Contains(definition)) trinkets.Add(definition);
                    }
                    else if (definition.m_type == ItemType.COMBAT) combat[definition] = inventory.GetItemQty(definition);
                    else if (!others.Contains(definition)) others.Add(definition);
                }

                // on an expedition a trinket that came off a hero is the party's to carry
                var toBag = _emptied && run != null && !run.AwaitsResume;
                var room = toBag ? run.Bag.FreeSlots : 0;
                foreach (var definition in trinkets)
                {
                    var qty = inventory.GetItemQty(definition);
                    if (!dry) inventory.RemoveItem(definition, qty);
                    for (var i = 0; i < qty; i++)
                    {
                        if (toBag && (dry ? room-- > 0 : run.TakeIntoBag(definition.m_id))) report.ToBag.Add(definition.m_id);
                        else
                        {
                            report.Trinkets.Add(definition.m_id);
                            if (!dry) Trinkets.Put(definition.m_id);
                        }
                    }
                }

                // what the estate has no ledger for
                foreach (var definition in others)
                {
                    var qty = inventory.GetItemQty(definition);
                    report.Removed.Add(definition.m_id + " x" + qty);
                    if (!dry) inventory.RemoveItem(definition, qty);
                }

                // combat items: out of the player inventory, and after a load out of the heroes' own slots
                if (!dry)
                    foreach (var pair in combat) inventory.RemoveItem(pair.Key, pair.Value);
                if (heroSlots)
                    foreach (var guid in RosterLifecycle.LivingGuids())
                    {
                        var slots = UpgradeUi.Hero(guid)?.GetCombatSkillInventory();
                        if (slots == null) continue;
                        foreach (var item in slots.GetValidItems())
                        {
                            combat.TryGetValue(item.GetItemDefinition(), out var have);
                            combat[item.GetItemDefinition()] = have + item.GetQty();
                        }
                        // Clear empties the slots and takes any that were added past the hero's own number away
                        if (!dry) slots.Clear();
                    }
                Settle(combat, run, dry, report);

                // the slots DD2 added past its limit to hold all this go with it
                if (!dry && report.Any) inventory.RemoveEmptyOverlimitItems();

                // DD2's "mastery points": spent at an inn's trainer, which the estate has not got
                var points = game.RunValues.GetValue(RunValueType.HERO_UPGRADE_POINTS);
                if (points > 0f)
                {
                    report.HeroPoints = points;
                    if (!dry) game.RunValues.SetValue(RunValueType.HERO_UPGRADE_POINTS, 0f);
                }

                // What a save carried for DD2's next loot window (a hero's gear from before the estate kept its
                // own books): the trinkets join those of the fallen, the rest has no place.
                report.LootWindow.AddRange(Dd2Loot.TakeNextWindow(game.LootManager, dry));
                // The trinkets of heroes who died where no expedition is on to claim them (in the hamlet) lie in
                // the stores. In a dungeon they are the expedition's to offer (DungeonRun).
                if (run == null)
                    foreach (var id in dry ? new List<string>(Dd2Loot.FallenTrinkets) : Dd2Loot.ClaimFallen())
                    {
                        report.Trinkets.Add(id);
                        if (!dry) Trinkets.Put(id);
                    }
            }
            catch (Exception e) { Plugin.Log.LogError("Estate: DD2's inventory could not be swept: " + e); }
            if (!dry) _emptied = true;
            return report;
        }

        // DD2 combat items found lying about. The twin of a DD1 supply joins the bag of the expedition under way
        // as that supply, as far as the bag has room. The rest is paid for at DD1's sell value (the twin's own;
        // for an item DD1 has nothing like, that of DD1's cheapest supply: GUESS, the plan's "5 to 25 of DD1's
        // gold each" names no rule), or is simply gone, by the owner's choice.
        private static void Settle(Dictionary<ItemDefinition, int> combat, DungeonRun run, bool dry, Report report)
        {
            if (combat.Count == 0) return;
            var items = InventoryContent.Items;
            var cheapest = items.All.Where(i => i.Type == ItemTypes.Supply && i.SellGold > 0).Select(i => i.SellGold).DefaultIfEmpty(0).Min();
            var room = new Dictionary<ItemDef, int>();
            foreach (var pair in combat.OrderBy(p => p.Key.m_id, StringComparer.Ordinal))
            {
                var qty = pair.Value;
                if (qty <= 0) continue;
                var supplyId = InventoryRaidRules.SupplyOfDd2Item(pair.Key.m_id);
                var supply = supplyId != null ? items.Get(ItemTypes.Supply, supplyId) : null;
                if (supply != null && run != null)
                {
                    int joined;
                    if (dry)
                    {
                        if (!room.ContainsKey(supply)) room[supply] = run.Bag.Room(supply);
                        joined = Math.Min(qty, room[supply]);
                        room[supply] -= joined;
                    }
                    else joined = qty - run.Bag.Add(supply, qty);
                    if (joined > 0) report.Supplies.Add(pair.Key.m_id + " x" + joined + " as " + supplyId);
                    qty -= joined;
                }
                if (qty <= 0) continue;
                var worth = PayForStrayCombatItems ? (supply != null ? supply.SellGold : cheapest) * qty : 0;
                report.PaidGold += worth;
                report.Paid.Add(pair.Key.m_id + " x" + qty + (PayForStrayCombatItems ? ": " + worth : ""));
            }
            if (!dry && report.PaidGold > 0) EstateState.AddGold(report.PaidGold, "dd2 combat items");
        }
    }
}
