using System;
using System.Collections.Generic;
using Assets.Code.Actor;
using Assets.Code.Game;
using Assets.Code.Item;
using Assets.Code.Run;
using Assets.Code.UI.Managers;
using Assets.Code.Utils;
using DD2Estate.Dd2;
using DD2Estate.Dev;
using DD2Estate.Dungeon;

namespace DD2Estate.Estate
{
    /// <summary>
    /// The measuring stick of "one inventory" (docs/recon/inventory-unification.md): everything a player owns
    /// or carries, as the estate keeps it and as DD2 keeps it, side by side. Read-only.
    ///
    ///   python tools/bridge.py run ledger.state
    ///
    /// "estate": the purse, the heirlooms, the unworn trinkets with their DD1 grades, the bag of the
    /// expedition and the trinkets found on it. "dd2": the player inventory (slots, limit, overflow, what lies in it by type), every hero's
    /// trinket and combat item slots, the hero upgrade points, the flame, and the loot waiting for DD2's next
    /// loot window. "clean" is true when DD2 holds nothing the estate does not know of: an empty player
    /// inventory, no combat items on anybody, no hero upgrade points, nothing waiting for a loot window.
    ///
    /// The books: ledger.purse is the purse's journal (every change since the game started, with what for and
    /// whether an expedition was under way); ledger.books is the record of the expedition that ended last (the
    /// quest's pay, the bag's worth, the purse before and after, and whether they agree). tools/ledger_check.py
    /// reads both.
    /// </summary>
    [EstateModule]
    internal static class LedgerDev
    {
        private static void Register()
        {
            AgentBridge.Register("ledger.state", o => Describe());
            // The purse's journal: {"since":N} the entries from number N on (default: the last twenty); "next" is
            // the number to ask from next time. Each entry: the week, the change, the purse after it, what for (a
            // word, or the class that asked) and where ("expedition" while a party is out and its expedition has
            // not ended: there should be none of those; "hamlet" otherwise, an expedition's end included).
            // "sum" adds the listed changes up; "byWhy" the same, apart.
            AgentBridge.Register("ledger.purse", o =>
            {
                var journal = EstateState.PurseJournal;
                var since = (int?)o["since"] ?? Math.Max(0, EstateState.PurseJournalNext - 20);
                var entries = new List<object>();
                var byWhy = new SortedDictionary<string, int>();
                var sum = 0;
                foreach (var entry in journal)
                {
                    if (entry.N < since) continue;
                    entries.Add(new { n = entry.N, week = entry.Week, amount = entry.Amount, after = entry.After, why = entry.Why, where = entry.Where });
                    byWhy.TryGetValue(entry.Why, out var have);
                    byWhy[entry.Why] = have + entry.Amount;
                    sum += entry.Amount;
                }
                return new
                {
                    gold = EstateState.Gold, week = EstateState.Current.Week, next = EstateState.PurseJournalNext,
                    // false when older entries than asked for have been dropped from the journal (it keeps the last few hundred)
                    complete = journal.Count == 0 || journal[0].N <= since,
                    since, sum, byWhy, entries
                };
            });
            // The record of the expedition that ended last in this session: what the quest paid, what the bag was
            // worth, the purse before and after, heirlooms and trinkets from both,
            // and "agrees": the purse moved by exactly the quest's pay and the haul. Null before any has ended.
            AgentBridge.Register("ledger.books", o => ExpeditionBooks.Last?.Describe());
            // One pass of the sweep that keeps DD2's player inventory empty (Dd2Sweep): gold into the purse,
            // trinkets into the estate's stores (on an expedition: into the bag), a DD2 combat item into the bag as
            // the DD1 supply it is the twin of or paid for, everything else removed, hero upgrade points to
            // nought. {"dry":true} moves nothing and says what would move. {"heroes":true} empties the heroes'
            // combat item slots too, as the pass after a load does.
            AgentBridge.Register("ledger.sweep", o =>
            {
                if (!EstateSession.Active) return "the Estate is not open";
                var dry = (bool?)o["dry"] ?? false;
                return Sweep(Dd2Sweep.Run(dry, (bool?)o["heroes"] ?? false), dry);
            });
            // The owner's choice Q7: {"on":true} stray DD2 combat items are paid for at DD1's sell value of a supply
            // (the default), {"on":false} they are removed without pay. Not saved.
            AgentBridge.Register("ledger.pay", o =>
            {
                if (o["on"] != null) Dd2Sweep.PayForStrayCombatItems = (bool)o["on"];
                return new { payForStrayCombatItems = Dd2Sweep.PayForStrayCombatItems };
            });
            // What loading the save did to bring it over: the format it was written in and what the pass after
            // the load moved ("loaded": "told" is its line in the Activity Log). {"dry":true} adds what a pass
            // would move now; without it the pass is made (and written into the Activity Log if it moved
            // anything). With the sweep held back (ledger.hold) that is the migration itself, looked at before it
            // is done: ledger.hold on=true at the main menu, estate.enter, ledger.migrate dry=true, ledger.migrate.
            AgentBridge.Register("ledger.migrate", o =>
            {
                if (!EstateSession.Active) return "the Estate is not open";
                var dry = (bool?)o["dry"] ?? false;
                var run = DungeonRun.Current;
                return new
                {
                    savedFormat = Dd2Sweep.LoadedFormat, format = EstateState.Format, held = Dd2Sweep.Hold,
                    loaded = Dd2Sweep.LastLoad != null ? Sweep(Dd2Sweep.LastLoad, false) : null,
                    // an older save's trinkets found on the way: out of the list beside the bag into the bag
                    loadedExpedition = DungeonRun.LastSideTrinkets,
                    now = Sweep(Dd2Sweep.Migrate(dry), dry),
                    nowExpedition = run != null && !run.AwaitsResume ? run.MoveSideTrinkets(dry) : null
                };
            });
            // Puts a DD2 item into DD2's player inventory the way DD2's own code does it (a trinket taken off a
            // hero who may no longer wear it, a loot window): {"id":"gold","qty":40}, {"id":"<a trinket's id>"}.
            // THE ESTATE CHANGES: with the sweep on, the item is out of there within a second, in the purse or
            // in the stores. To see it lie there first: ledger.hold on=true, then ledger.sweep dry=true.
            AgentBridge.Register("ledger.stray", o =>
            {
                if (!EstateSession.InHub) return "the Estate's hub is not up";
                var item = Trinkets.Find((string)o["id"]);
                var inventory = Singleton<GameTypeMgr>.Instance.PlayerInventory;
                if (item == null || inventory == null) return "no such DD2 item";
                var qty = Math.Max(1, (int?)o["qty"] ?? 1);
                inventory.AddItemsWithOverflow(item, qty, isPurchase: false);
                return new { put = item.m_id, qty, held = Dd2Sweep.Hold, wouldMove = Sweep(Dd2Sweep.Run(true), true) };
            });
            // Holds the sweep back, after a load and in the hub ({"on":true}), or lets it go again ({"on":false}).
            // Works at the main menu. A dev switch: it is not saved and is off when the game starts.
            AgentBridge.Register("ledger.hold", o =>
            {
                if (o["on"] != null) Dd2Sweep.Hold = (bool)o["on"];
                return new { held = Dd2Sweep.Hold };
            });
            // DD2's screens over the estate's things, as they stand: whether DD2 would open its inventory screen
            // (never, in the Estate), whether its character sheet is up and lets what a hero wears be changed.
            AgentBridge.Register("ledger.screens", o => Screens());
            // Asks DD2 for its inventory screen the way a click on a slot of its character sheet does. In the
            // Estate nothing opens: "inventoryOpen" stays false.
            AgentBridge.Register("ledger.dd2inventory", o =>
            {
                if (!EstateSession.Active) return "the Estate is not open";
                SingletonMonoBehaviour<CommonUiBhv>.Instance.TryShowPlayerInventory();
                return Screens();
            });
            // Opens DD2's character sheet the way DD2's own keys do, asking for inventory editing: {"guid":3}
            // (no guid: the first hero of the roster). In the Estate "sheetEditsInventory" stays false.
            // {"close":true} puts the sheet away.
            AgentBridge.Register("ledger.sheet", o =>
            {
                if (!EstateSession.Active) return "the Estate is not open";
                var ui = SingletonMonoBehaviour<CommonUiBhv>.Instance;
                if ((bool?)o["close"] == true)
                {
                    ui.HideCharacterSheet();
                    return Screens();
                }
                var guid = (uint?)o["guid"] ?? 0u;
                if (guid == 0u)
                    foreach (var living in RosterLifecycle.LivingGuids())
                    {
                        guid = living;
                        break;
                    }
                if (UpgradeUi.Hero(guid) == null) return "no such hero";
                ui.ShowCharacterSheet(null, guid, isSkillEditable: true, isInventoryEditable: true, autoselectTrinketSlot: true, heroSelectFilterParty: false);
                return Screens();
            });
        }

        private static object Sweep(Dd2Sweep.Report report, bool dry)
        {
            return new
            {
                dry, moved = report.Any, gold = report.Gold, trinkets = report.Trinkets, toBag = report.ToBag, supplies = report.Supplies,
                paid = report.Paid, paidGold = report.PaidGold, removed = report.Removed, heroUpgradePoints = report.HeroPoints,
                lootWindow = report.LootWindow, words = report.ToString(), told = report.Any ? report.Told() : null
            };
        }

        private static object Screens()
        {
            if (!SingletonMonoBehaviour<CommonUiBhv>.HasInstance()) return "DD2's UI is not up";
            var ui = SingletonMonoBehaviour<CommonUiBhv>.Instance;
            return new
            {
                estate = EstateSession.Active,
                mode = GameModeMgr.CurrentMode?.GetName(),
                canShowDd2Inventory = ui.GetCanShowPlayerInventory(),
                inventoryOpen = ui.IsInventoryActive,
                sheetOpen = ui.IsCharacterSheetOpen,
                sheetEditsInventory = ui.IsCharacterSheetActiveAndInventoryEditable
            };
        }

        public static object Describe()
        {
            if (!EstateSession.Active) return "the Estate is not open";
            var game = Singleton<GameTypeMgr>.Instance;
            var strays = new List<string>();

            var player = Inventory(game.PlayerInventory, out var held);
            if (held > 0) strays.Add(held + " item(s) in DD2's player inventory");

            var heroes = new List<object>();
            foreach (var guid in RosterLifecycle.LivingGuids())
            {
                var actor = UpgradeUi.Hero(guid);
                if (actor == null) continue;
                var combat = Items(actor.GetCombatSkillInventory(), out var carried);
                if (carried > 0) strays.Add(actor.ActorName + " carries " + carried + " combat item(s)");
                heroes.Add(new
                {
                    guid, name = actor.ActorName, cls = actor.ActorDataId, inParty = actor.IsInParty,
                    trinkets = RealmInventory.Worn(guid), trinketSlots = Slots(actor.GetTrinketInventory()),
                    combat, combatSlots = Slots(actor.GetCombatSkillInventory())
                });
            }

            float points = 0f, torch = 0f;
            try
            {
                points = game.RunValues.GetValue(RunValueType.HERO_UPGRADE_POINTS);
                torch = game.RunValues.GetValue(RunValueType.TORCH);
            }
            catch (Exception e) { Plugin.Log.LogWarning("Ledger: DD2's run values could not be read: " + e.Message); }
            if (points > 0f) strays.Add(points + " hero upgrade point(s)");

            var waiting = new List<object>();
            var loot = game.LootManager;
            if (loot != null)
                foreach (var item in loot.NextWindowItems)
                    if (item != null) waiting.Add(Item(item));
            if (waiting.Count > 0) strays.Add(waiting.Count + " item(s) waiting for DD2's loot window");

            var run = DungeonRun.Current;
            var stored = new List<object>();
            foreach (var id in Trinkets.Stored()) stored.Add(new { id, name = Trinkets.Name(id), grade = Trinkets.Grade(id) });
            var inBag = new List<object>();
            var onScroll = new List<object>();
            if (run != null)
            {
                foreach (var id in run.BagTrinkets()) inBag.Add(new { id, name = Trinkets.Name(id), grade = Trinkets.Grade(id) });
                foreach (var find in run.LootOnScroll) onScroll.Add(new { source = FoundLoot.Key(find.Source), items = ProvisionScreen.StackList(find.Stacks) });
            }
            return new
            {
                clean = strays.Count == 0,
                strays,
                estate = new
                {
                    format = EstateState.Format,
                    savedFormat = Dd2Sweep.LoadedFormat,
                    sweepHeld = Dd2Sweep.Hold,
                    gold = EstateState.Gold,
                    // where the purse's journal stands (ledger.purse since=<this> lists what moved it from here on)
                    purseJournalNext = EstateState.PurseJournalNext,
                    week = EstateState.Current.Week,
                    heirlooms = EstateState.Current.Heirlooms,
                    trinkets = stored,
                    expedition = run == null ? null : new
                    {
                        slots = run.Bag.SlotCount, free = run.Bag.FreeSlots, bag = ProvisionScreen.BagList(run.Bag),
                        // the unworn trinkets among the bag's items, each with the DD1 rarity it was found under
                        trinkets = inBag,
                        // what lies on DD1's loot scroll and what waits for its turn there; what the fight in
                        // progress pays if it is won
                        lootOnScroll = onScroll,
                        fightPays = Pays(run),
                        // an older save's trinkets beside the bag that are not in it yet (only while ledger.hold is on)
                        trinketsBesideTheBag = run.SideTrinkets,
                        light = run.Exploration.Light
                    }
                },
                dd2 = new
                {
                    mode = GameModeMgr.CurrentMode?.GetName(),
                    inventory = player,
                    heroes,
                    heroUpgradePoints = points,
                    torch,
                    nextLootWindow = waiting,
                    // loot DD2 wanted to grant and the estate did not take (the last of it, newest last)
                    lootTurnedAway = new { calls = Dd2Loot.SkippedCalls, last = Dd2Loot.SkippedLoot },
                    // trinkets of heroes who died, not yet claimed by the expedition or swept into the stores
                    fallenTrinkets = Dd2Loot.FallenTrinkets
                }
            };
        }

        // An inventory of DD2's as the bridge tells it; the count of what lies in it comes back in "held".
        private static object Inventory(ItemInventory inventory, out int held)
        {
            held = 0;
            if (inventory == null) return null;
            var items = Items(inventory, out held);
            var byType = new Dictionary<string, int>();
            foreach (var item in inventory.GetValidItems())
            {
                var type = item.GetItemType()?.GetName() ?? "?";
                byType.TryGetValue(type, out var have);
                byType[type] = have + Math.Max(1, item.GetQty());
            }
            return new
            {
                slots = inventory.GetNumberOfTotalSlots(), used = inventory.GetNumberOfFilledSlots(), limit = inventory.InventoryLimit,
                overflow = inventory.OverfilledCount, byType, items
            };
        }

        private static List<object> Items(ItemInventory inventory, out int count)
        {
            var items = new List<object>();
            count = 0;
            if (inventory == null) return items;
            foreach (var item in inventory.GetValidItems())
            {
                items.Add(Item(item));
                count += Math.Max(1, item.GetQty());
            }
            return items;
        }

        private static int Slots(ItemInventory inventory) => inventory != null ? inventory.GetNumberOfTotalSlots() : 0;

        private static List<string> Pays(DungeonRun run)
        {
            var pays = new List<string>();
            foreach (var draw in run.FightPays) pays.Add(draw.Table + "x" + draw.Draws);
            return pays;
        }

        private static object Item(IReadOnlyItemInstance item)
        {
            var definition = item.GetItemDefinition();
            return new { id = definition.m_id, type = definition.m_type?.GetName(), subType = definition.SubType?.m_Id, qty = item.GetQty() };
        }
    }
}
