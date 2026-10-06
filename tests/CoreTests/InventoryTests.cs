using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DD2Estate.Core;
using Newtonsoft.Json.Linq;

namespace DD2Estate.CoreTests
{
    public static class InventoryTests
    {
        private static ItemCatalog _items;
        private static InventoryRaidRules _rules;
        private static ProvisionRules _provision;

        private static ItemCatalog Items => _items ?? (_items = ItemCatalog.Load(Dd1.Files));
        private static InventoryRaidRules Rules => _rules ?? (_rules = InventoryRaidRules.Load(Dd1.Files));
        private static ProvisionRules Provision => _provision ?? (_provision = ProvisionRules.Load(Dd1.Files, Items));

        private static readonly ItemDef Coin = new ItemDef { Type = ItemTypes.Gold, StackLimit = 100 };
        private static readonly ItemDef Bread = new ItemDef { Type = ItemTypes.Provision, StackLimit = 4, BuyGold = 10, SellGold = 1, IconVariants = 4 };
        private static readonly ItemDef Spade = new ItemDef { Type = ItemTypes.Supply, Id = "spade", StackLimit = 2, BuyGold = 30, SellGold = 3 };
        private static readonly ItemDef Pearl = new ItemDef { Type = ItemTypes.Gem, Id = "pearl", StackLimit = 5, SellGold = 200 };
        private static readonly ItemDef Seal = new ItemDef { Type = ItemTypes.Heirloom, Id = "seal", StackLimit = 3 };

        // ---- the bag (no DD1 needed) ----

        public static void BagStacksFillThenTakeFreeSlots()
        {
            var bag = new Inventory(3);
            Check.True(bag.IsEmpty && bag.FreeSlots == 3 && bag.Room(Bread) == 12, "an empty bag");
            Check.Equal(0, bag.Add(Bread, 3), "three fit");
            Check.Equal(0, bag.Add(Spade, 1), "another item takes the next slot");
            Check.Equal(0, bag.Add(Bread, 2), "tops up, then a new stack");
            Check.True(bag.Slot(0).Item == Bread && bag.Slot(0).Amount == 4, "the first stack is full: " + bag.Slot(0));
            Check.True(bag.Slot(1).Item == Spade && bag.Slot(1).Amount == 1, "slot 1: " + bag.Slot(1));
            Check.True(bag.Slot(2).Item == Bread && bag.Slot(2).Amount == 1, "slot 2: " + bag.Slot(2));
            Check.Equal(5, bag.Count(Bread), "count over stacks");
            Check.Equal(3, bag.Room(Bread), "room left for bread");
            Check.Equal(1, bag.Room(Spade), "room left for spades");
            Check.Equal(0, bag.Room(Pearl), "no slot for a new kind");

            Check.Equal(4, bag.Add(Bread, 7), "overflow is reported");
            Check.Equal(8, bag.Count(Bread), "what fitted is in");
            Check.Equal(2, bag.Add(Pearl, 2), "nothing of a new kind fits a full bag");
            Check.Equal(0, bag.Add(Pearl, 0) + bag.Add(null, 5), "nothing to add");
            Check.True(bag.Slot(-1) == null && bag.Slot(3) == null, "slots outside the bag");
        }

        public static void BagRemovesFromTheLastStackAndKeepsSlots()
        {
            var bag = new Inventory(4);
            bag.Add(Bread, 6);      // slots 0 (4) and 1 (2)
            bag.Add(Spade, 2);      // slot 2
            Check.Equal(3, bag.Remove(Bread, 3), "removed");
            Check.True(bag.Slot(1) == null && bag.Slot(0).Amount == 3, "the later stack goes first, then the earlier one");
            Check.True(bag.Slot(2).Item == Spade, "other stacks stay where they are");
            Check.Equal(0, bag.Add(Pearl, 1), "a new item");
            Check.True(bag.Slot(1).Item == Pearl, "takes the first empty slot");
            Check.Equal(3, bag.Remove(Bread, 10), "cannot remove more than there is");
            Check.Equal(0, bag.Remove(Bread, 1), "none left");
            Check.Equal(1, bag.RemoveAt(2, 1), "one from a slot");
            Check.Equal(1, bag.RemoveAt(2, 5), "the rest of the slot");
            Check.True(bag.Slot(2) == null && bag.RemoveAt(2, 1) == 0, "the slot is empty");
            var dropped = bag.Clear(1);
            Check.True(dropped.Item == Pearl && dropped.Amount == 1 && bag.IsEmpty && bag.Clear(1) == null, "dropping a stack");
        }

        public static void BagStacksAreCarriedFromSlotToSlot()
        {
            var bag = new Inventory(4);
            bag.Add(Bread, 6);      // slots 0 (4) and 1 (2)
            bag.Add(Spade, 1);      // slot 2
            Check.True(bag.Move(2, 3) && bag.Slot(2) == null && bag.Slot(3).Item == Spade, "into an empty slot");
            Check.True(bag.Move(3, 0) && bag.Slot(0).Item == Spade && bag.Slot(3).Item == Bread && bag.Slot(3).Amount == 4, "two kinds change places");
            Check.True(bag.Move(3, 1) && bag.Slot(1).Amount == 4 && bag.Slot(3).Amount == 2, "a stack of the same kind is topped up, the rest stays");
            Check.True(bag.Move(3, 2) && bag.Slot(3) == null && bag.Slot(2).Amount == 2, "moved again");
            Check.True(bag.Move(2, 1) && bag.Slot(1).Amount == 2 && bag.Slot(2).Amount == 4, "onto a full stack of its kind: they change places");
            Check.True(!bag.Move(3, 0) && !bag.Move(0, 0) && !bag.Move(0, 9) && !bag.Move(-1, 0), "nothing to carry, nowhere to put it");
            Check.Equal(6, bag.Count(Bread), "nothing was lost");
            Check.Equal(1, bag.Count(Spade), "nothing was lost");
        }

        public static void BagWithoutStackLimitsIsAStoreShelf()
        {
            var shelf = new Inventory(2, useStackLimits: false);
            Check.Equal(0, shelf.Add(Bread, 40), "any amount in one slot");
            Check.True(shelf.Slot(0).Amount == 40 && shelf.FreeSlots == 1, "one slot used");
        }

        public static void BagSurvivesASave()
        {
            var catalog = ItemCatalog.Load(new NoDd1Files());
            var food = catalog.Food;
            var torch = catalog.Find("torch");
            var bag = new Inventory(6);
            bag.Add(food, food.StackLimit + 3);
            bag.Add(torch, 5);
            bag.Add(catalog.Ensure(ItemTypes.Gem, "moonstone"), 1);
            bag.Remove(food, 3);    // leaves a hole in slot 1

            var saved = JObject.Parse(bag.ToJson().ToString());
            var loaded = Inventory.FromJson(saved, ItemCatalog.Load(new NoDd1Files()), 16);
            Check.Equal(6, loaded.SlotCount, "slot count");
            for (var i = 0; i < bag.SlotCount; i++)
            {
                var a = bag.Slot(i);
                var b = loaded.Slot(i);
                Check.True(a == null ? b == null : b != null && b.Item.Key == a.Item.Key && b.Amount == a.Amount, "slot " + i + ": " + a + " / " + b);
            }
            Check.Equal("gem:moonstone", loaded.Slot(3).Item.Key, "an item the catalogue does not list comes back");
            Check.Equal(bag.ToJson().ToString(), loaded.ToJson().ToString(), "same save again");

            var empty = Inventory.FromJson(null, catalog, 16);
            Check.True(empty.SlotCount == 16 && empty.IsEmpty, "no save, an empty bag");
            var broken = Inventory.FromJson(JObject.Parse("{\"slots\":2,\"items\":[{\"slot\":9,\"type\":\"provision\",\"n\":3},{\"slot\":0,\"type\":\"provision\",\"n\":1},{\"type\":\"\",\"n\":4},{\"type\":\"gold\",\"n\":-2}]}"), catalog, 16);
            Check.Equal(4, broken.Count(catalog.Food), "stacks with a bad slot are still in the bag");
        }

        public static void HaulIsWhatTheEstateKeeps()
        {
            var bag = new Inventory(8);
            bag.Add(Coin, 150);
            bag.Add(Pearl, 2);
            bag.Add(Seal, 4);
            bag.Add(Bread, 4);
            bag.Add(Spade, 1);
            bag.Add(new ItemDef { Type = ItemTypes.QuestItem, Id = "relic", SellGold = 999 }, 1);

            var home = bag.Settle(type => 1);
            Check.Equal(150, home.Coins, "coins");
            Check.Equal(400, home.GemGold, "gems at their sell value");
            Check.Equal(4 * 1 + 3, home.SupplyGold, "leftover supplies at their sell value");
            Check.Equal(557, home.Gold, "total");
            Check.True(home.Heirlooms.Count == 1 && home.Heirlooms["seal"] == 4, "heirlooms");

            var fled = bag.Settle(type => type == ItemTypes.Provision || type == ItemTypes.Supply ? 0.25 : 1);
            Check.Equal(150 + 400 + 1, fled.Gold, "a quarter of the supplies, rounded down per stack");
            Check.Equal(0, bag.Settle(type => 0).Gold + bag.Settle(type => 0).Heirlooms.Count, "nothing comes back");
            Check.Equal(557, bag.Settle(null).Gold, "no rates: everything");
        }

        public static void ATrinketIsCarriedInASlotOfItsOwn()
        {
            var catalog = ItemCatalog.Load(new NoDd1Files());
            var ring = catalog.Trinket("ring_of_test");
            Check.True(ring != null && ring.Type == ItemTypes.Trinket && ring.Id == "ring_of_test" && ring.StackLimit == 1, "a trinket as a bag item: " + ring);
            Check.True(catalog.Find("trinket:ring_of_test") == ring, "found again by its key");
            Check.True(catalog.Find("trinket:another_one") != null && catalog.Find("trinket:another_one") != ring, "one the bag has not met yet");
            Check.True(catalog.Trinket("") == null && catalog.Trinket(null) == null, "no id, no trinket");
            Check.True(ring.CurioItemId == null, "no curio takes a trinket");

            var bag = new Inventory(4);
            Check.Equal(0, bag.Add(ring, 2), "two of a kind");
            Check.True(bag.Slot(0).Amount == 1 && bag.Slot(1).Amount == 1 && bag.Slot(0).Item == ring && bag.Slot(1).Item == ring, "do not stack: a slot each");
            Check.Equal(0, bag.Add(Coin, 80), "coins beside them");
            Check.Equal(0, bag.Add(catalog.Trinket("amulet"), 1), "another trinket");
            Check.Equal(1, bag.Add(catalog.Trinket("third"), 1), "a full bag takes no trinket");
            Check.Equal(0, bag.Room(ring), "and has no room for one");

            // home: to the trinket storage, in the bag's order, and not among the cards of the treasure
            var home = bag.Settle(null);
            Check.Equal("ring_of_test,ring_of_test,amulet", string.Join(",", home.Trinkets), "the trinkets that came home");
            Check.True(home.Stacks.All(s => s.Item.Type != ItemTypes.Trinket), "no treasure card for a trinket");
            Check.Equal(80, home.Gold, "a trinket is worth no gold on the way home");
            Check.Equal(0, RaidResultRules.TreasureCards(home).Count(c => c.Item.Type == ItemTypes.Trinket), "none on the results screen's treasure row");

            // a failed quest: DD1's keep rate is each trinket's chance
            Check.Equal(0, bag.Settle(type => 0).Trinkets.Count, "a lost party's trinkets");
            Check.Equal(3, bag.Settle(type => 1, new Rng(1)).Trinkets.Count, "rate 1 keeps all");
            Check.Equal(0, bag.Settle(type => type == ItemTypes.Trinket ? 0.5 : 1).Trinkets.Count, "half of one is none, without dice");
            var kept = 0;
            var rng = new Rng(11);
            for (var i = 0; i < 400; i++) kept += bag.Settle(type => type == ItemTypes.Trinket ? 0.5 : 1, rng).Trinkets.Count;
            Check.Near(600, kept, 60, "half of them over many retreats");

            // and through a save
            var loaded = Inventory.FromJson(JObject.Parse(bag.ToJson().ToString()), ItemCatalog.Load(new NoDd1Files()), 16);
            Check.Equal("trinket:ring_of_test", loaded.Slot(0).Item.Key, "a saved trinket");
            Check.Equal(bag.ToJson().ToString(), loaded.ToJson().ToString(), "same save again");
            Check.Equal(1, loaded.RemoveAt(1, 1), "one taken out to be worn");
            Check.Equal("ring_of_test,amulet", string.Join(",", loaded.Settle(null).Trinkets), "the rest comes home");
        }

        public static void StackPicturesFollowTheAmount()
        {
            Check.Equal("panels/icons_equip/supply/inv_supply+spade.png", Spade.IconPath(1), "one picture");
            Check.Equal("panels/icons_equip/provision/inv_provision+_0.png", Bread.IconPath(1), "a nearly empty stack");
            Check.Equal("panels/icons_equip/provision/inv_provision+_3.png", Bread.IconPath(4), "a full stack");
            var last = -1;
            for (var n = 0; n <= 20; n++)
            {
                var path = Bread.IconPath(n);
                var index = path[path.Length - 5] - '0';
                Check.True(index >= last && index >= 0 && index <= 3, "pictures never go back: " + path);
                last = index;
            }
            Check.Equal("supply:spade", Spade.Key, "key with an id");
            Check.Equal("provision", Bread.Key, "key without one");
            Check.True(Spade.CurioItemId == "spade" && Bread.CurioItemId == "provision" && Pearl.CurioItemId == null, "curio ids");
        }

        // ---- the shop ----

        public static void ShopWorksWithoutDd1()
        {
            var files = new NoDd1Files();
            var items = ItemCatalog.Load(files);
            var rules = InventoryRaidRules.Load(files);
            var provision = ProvisionRules.Load(files, items);
            Check.True(items.Food != null && items.Gold != null && items.Find("torch") != null, "fallback items");
            Check.True(rules.RaidSlots > 0 && rules.FoodBeforeFull > 0 && rules.FoodHeal > 0, "fallback rules");
            Check.Equal(1.0, rules.KeepRate(ItemTypes.Supply, RaidStatus.Abandoned), "no keep rates: everything is kept");
            Check.Equal(0.0, rules.EffectStress(new[] { "Stress 2" }), "no effects file");

            var shop = new ProvisionShop(provision, rules, 1, 50000);
            Check.True(shop.Lines.Count > 0 && shop.Lines.All(l => l.Stock > 0 && l.Item.BuyGold > 0), "a fallback stock");
            Check.Equal(1, shop.Buy(items.Food), "buying works");
        }

        public static void ShopBillsInDd1Gold()
        {
            var files = new NoDd1Files();
            var items = ItemCatalog.Load(files);
            var shop = new ProvisionShop(ProvisionRules.Load(files, items), InventoryRaidRules.Load(files), 1, 500);
            var food = items.Food;          // fallback price 75
            var shovel = items.Find("shovel");   // 250

            Check.Equal(0, shop.Cost, "nothing bought");
            Check.Equal(1, shop.Buy(food), "one food");
            Check.Equal(75, shop.Cost, "the bill is the price: 75 for one food");
            Check.Equal(1, shop.Buy(food), "another");
            Check.Equal(150, shop.Cost, "and 150 for two: nothing is rounded");
            Check.Equal((500 - 150) / 75, shop.CanBuy(food), "the purse limits the amount");
            Check.Equal(1, shop.Buy(shovel, 3), "only one shovel is affordable");
            Check.True(shop.Cost <= shop.Purse, "the bill never exceeds the purse");
            Check.Equal(1, shop.Buy(food, 50), "the last coins");
            Check.Equal(0, shop.Buy(food) + shop.CanBuy(shovel), "the purse is empty");
            Check.Equal(475, shop.Cost, "three food and a shovel, to the coin");

            Check.Equal(1, shop.SellBack(shovel, 5), "sold back what was bought");
            Check.Equal(0, shop.Bag.Count(shovel), "the shovel left the bag");
            Check.Equal(3 * 75, shop.Cost, "the full price comes back");
            Check.Equal(0, shop.SellBack(shovel), "nothing more to sell back");

            // what the party owns already is not on the bill and stays in the bag
            var torch = items.Find("torch");
            Check.Equal(0, shop.AddFree(torch, 2), "free items");
            Check.Equal(3 * 75, shop.Cost, "free items cost nothing");
            Check.Equal(0, shop.SellBack(torch), "free items cannot be sold");
            shop.Buy(torch, 1);
            Check.Equal(1, shop.SellBackAt(1, 5), "only the bought torch goes back from the slot");
            Check.Equal(2, shop.Bag.Count(torch), "the free ones stay");

            // stock and bag space
            var rich = new ProvisionShop(ProvisionRules.Load(files, items), InventoryRaidRules.Load(files), 1, 5000000);
            var line = rich.Line(food);
            Check.Equal(line.Stock, rich.Buy(food, 9999), "the shelf limits the amount");
            Check.True(line.Left == 0 && rich.CanBuy(food) == 0, "sold out");
            foreach (var other in rich.Lines) rich.Buy(other.Item, 9999);
            Check.True(rich.Bag.FreeSlots == 0 || rich.Lines.All(l => l.Left == 0), "the bag or the shelves limit the rest");
            Check.True(rich.Lines.All(l => l.Bought == rich.Bag.Count(l.Item)), "what is billed is what is in the bag");
        }

        public static void StandardKitIsBoughtAsFarAsThePurseGoes()
        {
            var files = new NoDd1Files();
            var items = ItemCatalog.Load(files);
            var provision = ProvisionRules.Load(files, items);
            var rules = InventoryRaidRules.Load(files);

            var rich = new ProvisionShop(provision, rules, 1, 50000);
            var kit = rich.StandardKit();
            Check.True(kit.Count == 4 && kit[0].Item == items.Food && kit.All(k => k.Amount > 0), "food first, then torches, a shovel, a key");
            Check.True(rich.BuyStandardKit(), "the whole kit");
            foreach (var want in kit) Check.Equal(want.Amount, rich.Bag.Count(want.Item), want.Item.Key);
            var cost = rich.Cost;
            Check.True(rich.BuyStandardKit() && rich.Cost == cost, "buying it again adds nothing");

            var poor = new ProvisionShop(provision, rules, 1, 250);
            Check.True(!poor.BuyStandardKit(), "not all of it");
            Check.True(poor.Bag.Count(items.Food) == 3 && poor.Cost <= 250, "food comes first: " + poor.Bag.Count(items.Food));
            Check.True(new ProvisionShop(provision, rules, 3, 50000).StandardKit()[1].Amount > kit[1].Amount, "longer quests take more torches");

            var broke = new ProvisionShop(provision, rules, 1, 0);
            Check.True(!broke.BuyStandardKit() && broke.Bag.IsEmpty && broke.Cost == 0, "an empty purse buys nothing");
        }

        // ---- against the install ----

        private static IEnumerable<Dictionary<string, string>> RawItems()
        {
            var dir = Path.Combine(Dd1.Root, "inventory");
            if (!Dd1.Available) throw new SkipException("needs the DD1 install");
            foreach (var file in Directory.GetFiles(dir, "base.*.inventory.items.darkest"))
                foreach (var line in File.ReadAllLines(file))
                {
                    if (!line.TrimStart().StartsWith("inventory_item:")) continue;
                    var fields = new Dictionary<string, string>();
                    foreach (Match m in Regex.Matches(line, "\\.(\\w+)\\s+(\"[^\"]*\"|\\S+)")) fields[m.Groups[1].Value] = m.Groups[2].Value.Trim('"');
                    yield return fields;
                }
        }

        public static void ItemsComeFromTheInstall()
        {
            var raw = RawItems().ToList();
            Check.True(raw.Count >= 30, "items in the files: " + raw.Count);
            foreach (var fields in raw)
            {
                var item = Items.Get(fields["type"], fields["id"]);
                Check.True(item != null, "item " + fields["type"] + ":" + fields["id"] + " is not in the catalogue");
                Check.Equal(int.Parse(fields["base_stack_limit"]), item.StackLimit, item.Key + " stack limit");
                Check.Equal(int.Parse(fields["purchase_gold_value"]), item.BuyGold, item.Key + " price");
                Check.Equal(int.Parse(fields["sell_gold_value"]), item.SellGold, item.Key + " sell value");
                Check.True(Dd1.Files.Exists(item.IconPath(1)) && Dd1.Files.Exists(item.IconPath(item.StackLimit)), item.Key + " has no icon: " + item.IconPath(1));
            }
            Check.Equal(raw.Select(f => f["type"] + ":" + f["id"]).Distinct().Count(), Items.All.Count, "nothing else in the catalogue");

            Check.True(Items.Food.IconVariants == 4 && Items.Gold.IconVariants == 4, "food and gold have stack pictures");
            Check.True(Items.Find("torch").IconVariants == 0 && Items.Find("holy_water").Type == ItemTypes.Supply, "a supply by its bare id");
            Check.True(Items.Find("quest_item:holy_water").Type == ItemTypes.QuestItem && Items.Find("ruby").Type == ItemTypes.Gem, "by key, by gem id");
            Check.True(Items.Find("food") == Items.Food && Items.Find("gold") == Items.Gold && Items.Find("no_such_thing") == null, "food and gold by name");
            Check.True(Items.Food.StackLimit > Items.Find("shovel").StackLimit && Items.Gold.StackLimit > 100, "stack sizes differ by item");
        }

        public static void InventoryRulesComeFromTheInstall()
        {
            var configs = Dd1.Files.ReadText("inventory/base.inventory.system_configs.darkest").Split('\n');
            var raid = configs.First(l => l.Contains("\"raid\""));
            Check.Equal(int.Parse(Regex.Match(raid, "\\.max_slots\\s+(\\d+)").Groups[1].Value), Rules.RaidSlots, "raid slots");
            Check.True(Rules.RaidStackLimits, "the raid bag uses stack limits");

            var raw = JObject.Parse(Dd1.Files.ReadText("shared/rules.json"));
            Check.Equal((double)raw["provision_hp_heal"], Rules.FoodHeal, "food heal");
            Check.Equal((int)raw["max_provisions_before_full"], Rules.FoodBeforeFull, "food before full");
            var none = ((JArray)raw["meals_table"]).First(m => (double)m["rations_per"] == 0);
            Check.Equal((double)none["stress"], Rules.StarveStress, "stress of going without a meal");
            Check.Equal(4, Rules.HungerFood(4), "a meal is one unit per hero");

            var estate = JObject.Parse(Dd1.Files.ReadText("campaign/estate/estate.json"));
            foreach (var rate in (JArray)estate["quest_fail_keep_rates"])
            {
                Check.Equal((double)rate["rate"], Rules.KeepRate((string)rate["type"], RaidStatus.Abandoned), "keep rate of " + rate["type"]);
                Check.Equal(1.0, Rules.KeepRate((string)rate["type"], RaidStatus.Succeeded), "a success keeps everything");
                Check.Equal(0.0, Rules.KeepRate((string)rate["type"], RaidStatus.Failed), "a lost party keeps nothing");
            }
            Check.True(Rules.KeepRate(ItemTypes.Supply, RaidStatus.Abandoned) < Rules.KeepRate(ItemTypes.Gold, RaidStatus.Abandoned), "supplies are mostly lost on a retreat");

            // the obstacle's fail effect is a stress effect with a number in the effects file
            var obstacle = Dd1.Raid.Obstacle;
            Check.True(Rules.EffectStress(obstacle.FailEffects) > 0, "stress of digging by hand: " + string.Join(",", obstacle.FailEffects));
            Check.True(Rules.EffectStress(new[] { "Stress 1" }) < Rules.EffectStress(new[] { "Stress 3" }), "stress effects grow");
            Check.Equal(Rules.EffectStress(new[] { "Stress 1" }) + Rules.EffectStress(new[] { "Stress 2" }), Rules.EffectStress(new[] { "Stress 1", "Stress 2", "no such effect" }), "summed, unknown names ignored");
            Check.Equal(0.0, Rules.EffectStress(null), "no effects");
        }

        public static void EveryShopItemHasAUseOrACurio()
        {
            var curioItems = new HashSet<string>(Dd1.Curios.All.SelectMany(c => c.ItemUses).Select(u => u.ItemId));
            Check.True(curioItems.Count >= 6, "items curios react to: " + string.Join(",", curioItems));
            foreach (var id in curioItems)
                Check.True(Items.ForCurio(id) != null && Items.ForCurio(id).CurioItemId == id, "curio item " + id + " is not an item of the catalogue");

            var obstacleItem = Items.ForCurio(Dd1.Raid.Obstacle.ClearItem);
            Check.True(obstacleItem != null, "the obstacle's item is in the catalogue");
            for (var length = 1; length <= 3; length++)
                foreach (var stack in Provision.Stock(length))
                {
                    var use = Rules.UseOf(stack.Item);
                    var useful = use.Kind != ItemUseKind.None || curioItems.Contains(stack.Item.CurioItemId) || stack.Item == obstacleItem;
                    Check.True(useful, stack.Item.Key + " is sold but does nothing");
                    if (use.NeedsHero) Check.True(use.Amount > 0, stack.Item.Key + ": amount");
                }
            Check.True(Rules.UseOf(Items.Find("torch")).Kind == ItemUseKind.Light && !Rules.UseOf(Items.Find("torch")).NeedsHero, "a torch needs no hero");
            Check.True(Rules.UseOf(Items.Food).Kind == ItemUseKind.Food && Rules.UseOf(Items.Food).Amount == Rules.FoodHeal, "food heals DD1's share");
            Check.True(Rules.UseOf(Items.Gold).Kind == ItemUseKind.None && Rules.UseOf(null).Kind == ItemUseKind.None, "gold has no use");
        }

        public static void SuppliesOfAFightHaveTheirDd2Twins()
        {
            // both ways, and nothing for what is no fight item in either game
            var twins = new Dictionary<string, string>
            {
                { "bandage", "bandages" }, { "antivenom", "antivenom" }, { "medicinal_herbs", "medicinal_herbs" }, { "holy_water", "holy_water" },
                { "laudanum", "laudanum" }, { "torch", "torch_consumable" }
            };
            foreach (var pair in twins)
            {
                Check.Equal(pair.Value, InventoryRaidRules.Dd2ItemOf(pair.Key), "the DD2 item of " + pair.Key);
                Check.Equal(pair.Key, InventoryRaidRules.SupplyOfDd2Item(pair.Value), "the DD1 supply of " + pair.Value);
            }
            foreach (var id in new[] { "shovel", "skeleton_key", "firewood", "provision", "", null })
                Check.True(InventoryRaidRules.Dd2ItemOf(id) == null, id + " is no fight item");
            foreach (var id in new[] { "smoke_bomb", "gold", "", null })
                Check.True(InventoryRaidRules.SupplyOfDd2Item(id) == null, id + " has no DD1 twin");
            // every twin is a supply of the player's DD1 with a sell value: what a stray one is paid at
            foreach (var supply in twins.Keys)
            {
                var item = Items.Get(ItemTypes.Supply, supply);
                Check.True(item != null && item.SellGold > 0, "DD1 has no supply " + supply + " with a sell value");
            }
        }

        public static void TheStandInUsesOfSuppliesCanBeTakenOut()
        {
            var catalog = ItemCatalog.Load(new NoDd1Files());
            var rules = InventoryRaidRules.Load(new NoDd1Files());
            Check.True(InventoryRaidRules.StandInSupplyUses, "they are in unless the owner says otherwise");
            Check.True(rules.UseOf(catalog.Find("bandage")).Kind == ItemUseKind.Heal && rules.UseOf(catalog.Find("laudanum")).Kind == ItemUseKind.StressHeal, "the stand-ins");
            try
            {
                InventoryRaidRules.StandInSupplyUses = false;
                foreach (var id in new[] { "bandage", "antivenom", "medicinal_herbs", "holy_water", "laudanum" })
                    Check.True(rules.UseOf(catalog.Find(id)).Kind == ItemUseKind.None, id + " still does something to a hero");
                // DD1's own uses stay
                Check.True(rules.UseOf(catalog.Find("torch")).Kind == ItemUseKind.Light, "a torch is still lit");
                Check.True(rules.UseOf(catalog.Food).Kind == ItemUseKind.Food, "food is still eaten");
                Check.True(rules.UseOf(catalog.Ensure(ItemTypes.Supply, "firewood")).Kind == ItemUseKind.Camp, "firewood still makes camp");
            }
            finally { InventoryRaidRules.StandInSupplyUses = true; }
        }

        // what the dungeon layer does at a curio: offer the carried items the curio reacts to, spend the one used
        public static void CarriedItemsWorkOnTheCuriosOfEveryDungeon()
        {
            var rng = new Rng(5);
            var offered = 0;
            foreach (var dungeon in Dd1.Dungeons)
            {
                var props = Dd1.Generation.PropsFor(dungeon);
                foreach (var prop in props.HallCurios.Concat(props.RoomCurios).Concat(props.RoomTreasures).Select(w => w.Id).Distinct())
                {
                    var bag = new Inventory(Rules.RaidSlots, Rules.RaidStackLimits);
                    foreach (var line in Provision.Stock(1)) bag.Add(line.Item, 1);
                    foreach (var id in Dd1.Curios.ItemsFor(prop))
                    {
                        var item = Items.ForCurio(id);
                        if (bag.Count(item) == 0) continue;     // dog treats: no shop sells them
                        offered++;
                        var outcome = Dd1.Curios.Roll(prop, item.CurioItemId, rng);
                        Check.Equal(id, outcome.ItemId, prop + " takes " + id);
                        Check.Equal(1, bag.Remove(item, 1), "the item is spent");
                    }
                    Check.True(Dd1.Curios.Roll(prop, null, rng).ItemId == null, prop + ": bare hands spend nothing");
                }
            }
            Check.True(offered >= 30, "item uses on offer across the dungeons: " + offered);
        }

        public static void ProvisionStockComesFromTheInstall()
        {
            var raw = JObject.Parse(Dd1.Files.ReadText("campaign/provision/provision.json"));
            var lists = (JArray)raw["default_store_inventory_item_lists"];
            for (var length = 1; length <= 3; length++)
            {
                var stock = Provision.Stock(length);
                var expected = ((JArray)lists[length]).Where(e => (int)e["amount"] > 0).ToList();
                Check.Equal(expected.Count, stock.Count, "items on the shelf, length " + length);
                for (var i = 0; i < stock.Count; i++)
                {
                    Check.True(stock[i].Item.Type == (string)expected[i]["type"] && stock[i].Item.Id == (string)expected[i]["id"], "shelf order, length " + length);
                    Check.Equal((int)expected[i]["amount"], stock[i].Amount, stock[i].Item.Key + " stock, length " + length);
                    Check.True(stock[i].Item.BuyGold > 0, stock[i].Item.Key + " has a price");
                }
                Check.Equal((int)raw["confirm_datas"][length]["minimum_food"], Provision.MinimumFood(length), "minimum food, length " + length);
            }
            Check.True(Provision.Stock(3).Sum(s => s.Amount) > Provision.Stock(1).Sum(s => s.Amount), "a long quest's shop holds more");
            Check.True(Provision.Stock(99).Count > 0 && Provision.Stock(-1).Count == 0, "lengths outside the lists");

            foreach (var entry in (JArray)raw["raid_starting_hero_class_item_lists"])
            {
                var items = Provision.ClassItems((string)entry["hero_class"]);
                Check.Equal(((JArray)entry["item_lists"]).Count, items.Count, "class items of " + entry["hero_class"]);
            }
            Check.Equal(0, Provision.ClassItems("no_such_class").Count + Provision.ClassItems(null).Count, "a class without items");
            Check.True(Provision.StartingItems(1).Count == 0 && Provision.StartingItems(2).Any(s => s.Item.Id == "firewood"), "firewood for the camps of longer quests");
        }

        public static void AShortQuestCanBeProvisioned()
        {
            var shop = new ProvisionShop(Provision, Rules, 1, 5000);
            Check.True(shop.Lines.Count <= shop.Bag.SlotCount, "one of everything fits the bag");
            Check.True(shop.BuyStandardKit(), "the standard kit");
            Check.True(shop.Food >= shop.MinimumFood && shop.MinimumFood > 0, "enough food not to be warned");
            Check.True(shop.Bag.Count(Items.Find("torch")) > 0 && shop.Bag.Count(Items.Find("shovel")) == 1, "torches and a shovel");
            Check.True(shop.Cost > 0 && shop.Cost < 3000, "a first quest's kit costs less than the quest pays: " + shop.Cost);

            // a short quest's whole shop fits the bag; a long quest's does not: slots and stack sizes are the limit, as in DD1
            var greedy = new ProvisionShop(Provision, Rules, 1, 5000000);
            foreach (var line in greedy.Lines) greedy.Buy(line.Item, 9999);
            Check.True(greedy.Lines.All(l => l.Left == 0) && greedy.Bag.FreeSlots > 0, "the short shop can be emptied");
            var hoarder = new ProvisionShop(Provision, Rules, 3, 5000000);
            foreach (var line in hoarder.Lines) hoarder.Buy(line.Item, 9999);
            Check.True(hoarder.Bag.FreeSlots == 0 && hoarder.Lines.Any(l => l.Left > 0), "the bag fills up before the long shop is empty");
            for (var i = 0; i < hoarder.Bag.SlotCount; i++)
                Check.True(hoarder.Bag.Slot(i).Amount <= hoarder.Bag.Slot(i).Item.StackLimit, "stack limit in slot " + i);
            Check.True(hoarder.Lines.All(l => l.Bought == hoarder.Bag.Count(l.Item)), "what is billed is what is in the bag");
        }

        public static void LootGoesIntoTheBag()
        {
            var rng = new Rng(11);
            var bag = new Inventory(Rules.RaidSlots, Rules.RaidStackLimits);
            var kinds = new HashSet<LootKind>();
            var left = 0;
            foreach (var dungeon in Dd1.Dungeons)
                foreach (var drop in Dd1.Loot.Draw("A", 40, 1, dungeon, rng))
                {
                    var item = Items.ForLoot(drop);
                    if (drop.Kind == LootKind.Trinket || drop.Kind == LootKind.JournalPage || drop.Kind == LootKind.Nothing)
                    {
                        Check.True(item == null, drop + " is not carried");
                        continue;
                    }
                    Check.True(item != null && drop.Amount > 0, drop + " has no item");
                    Check.True(Dd1.Files.Exists(item.IconPath(drop.Amount)), drop + " has no icon");
                    kinds.Add(drop.Kind);
                    left += bag.Add(item, drop.Amount);
                }
            Check.True(kinds.Contains(LootKind.Gold) && kinds.Contains(LootKind.Gem) && kinds.Contains(LootKind.Heirloom) && kinds.Contains(LootKind.Supply), "chests hold gold, gems, heirlooms and supplies");
            Check.True(bag.FreeSlots == 0 && left > 0, "160 chest draws do not fit sixteen slots");

            var haul = bag.Settle(type => Rules.KeepRate(type, RaidStatus.Succeeded));
            Check.True(haul.Coins > 0 && haul.Gold >= haul.Coins && haul.Heirlooms.Values.Sum() > 0, "the haul is worth something");
            Check.True(bag.Settle(type => Rules.KeepRate(type, RaidStatus.Abandoned)).Gold <= haul.Gold, "a retreat brings home less");
        }
    }
}
