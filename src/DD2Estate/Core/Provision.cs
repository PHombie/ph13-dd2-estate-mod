using System;
using System.Collections.Generic;
using System.Linq;

namespace DD2Estate.Core
{
    /// <summary>
    /// DD1's provisioning data (<c>campaign/provision/provision.json</c>): what the shop stocks for a quest of a
    /// given length, what the party is handed for free, and how much food the game calls too little. Lists are
    /// indexed by quest length (1 short, 2 medium, 3 long).
    /// </summary>
    public sealed class ProvisionRules
    {
        private readonly List<List<ItemStack>> _stock = new List<List<ItemStack>>();
        private readonly List<List<ItemStack>> _starting = new List<List<ItemStack>>();
        private readonly List<int> _minimumFood = new List<int>();
        private readonly Dictionary<string, List<ItemStack>> _classItems = new Dictionary<string, List<ItemStack>>();

        public static ProvisionRules Load(IDd1Files files, ItemCatalog items)
        {
            var rules = new ProvisionRules();
            var json = Json.ParseFile(files.ReadText("campaign/provision/provision.json"));
            if (json != null)
            {
                foreach (var list in Json.Array(json["default_store_inventory_item_lists"])) rules._stock.Add(ReadList(list, items));
                foreach (var list in Json.Array(json["raid_starting_length_inventory_item_lists"])) rules._starting.Add(ReadList(list, items));
                foreach (var data in Json.Array(json["confirm_datas"])) rules._minimumFood.Add(Json.Int(data["minimum_food"], 0));
                foreach (var entry in Json.Array(json["raid_starting_hero_class_item_lists"]))
                {
                    var heroClass = (string)entry["hero_class"];
                    if (heroClass != null) rules._classItems[heroClass] = ReadList(entry["item_lists"], items);
                }
            }
            if (rules._stock.All(l => l.Count == 0))
            {
                // FALLBACK for an install without provision.json: every item with a price, two stacks of each,
                // whatever the length. Not DD1's stock.
                rules._stock.Clear();
                rules._stock.Add(items.All.Where(i => i.BuyGold > 0).Select(i => new ItemStack { Item = i, Amount = i.StackLimit * 2 }).ToList());
            }
            return rules;
        }

        private static List<ItemStack> ReadList(Newtonsoft.Json.Linq.JToken list, ItemCatalog items)
        {
            var stacks = new List<ItemStack>();
            foreach (var entry in Json.Array(list))
            {
                var item = items.Get((string)entry["type"], (string)entry["id"] ?? "");
                var amount = Json.Int(entry["amount"], 0);
                if (item != null && amount > 0) stacks.Add(new ItemStack { Item = item, Amount = amount });
            }
            return stacks;
        }

        private static T ForLength<T>(List<T> lists, int length, T none)
        {
            if (lists.Count == 0) return none;
            return lists[Math.Max(0, Math.Min(lists.Count - 1, length))];
        }

        /// <summary>The shop's stock for a quest of this length, in DD1's shelf order.</summary>
        public IReadOnlyList<ItemStack> Stock(int length) => ForLength(_stock, length, new List<ItemStack>());

        /// <summary>What every party gets for free on a quest of this length (firewood for the camps).</summary>
        public IReadOnlyList<ItemStack> StartingItems(int length) => ForLength(_starting, length, new List<ItemStack>());

        /// <summary>Below this much food DD1 asks whether the party really wants to set out.</summary>
        public int MinimumFood(int length) => ForLength(_minimumFood, length, 0);

        /// <summary>What a hero of a DD1 class brings along (a crusader's holy water, a plague doctor's antivenom).</summary>
        public IReadOnlyList<ItemStack> ClassItems(string heroClass)
        {
            return heroClass != null && _classItems.TryGetValue(heroClass, out var list) ? list : new List<ItemStack>();
        }
    }

    public sealed class ShopLine
    {
        public ItemDef Item;
        public int Stock, Bought;
        /// <summary>Share of the item's price asked at this visit (a town event's discount); 1 as a rule.</summary>
        public double PriceFactor = 1.0;

        /// <summary>Gold for one unit at this visit.</summary>
        public int Price => (int)Math.Round(Item.BuyGold * PriceFactor);

        public int Left => Stock - Bought;
    }

    /// <summary>
    /// One visit to the provision shop before an expedition: the shelves, the party's bag and the bill. Prices
    /// and the purse are DD1's gold.
    /// </summary>
    public sealed class ProvisionShop
    {
        private readonly ProvisionRules _rules;
        private readonly List<ShopLine> _lines = new List<ShopLine>();

        public Inventory Bag { get; }
        public int Length { get; }
        /// <summary>What the estate can spend.</summary>
        public int Purse;

        public IReadOnlyList<ShopLine> Lines => _lines;

        public ProvisionShop(ProvisionRules rules, InventoryRaidRules inventory, int length, int purse)
        {
            _rules = rules;
            Length = length;
            Purse = Math.Max(0, purse);
            Bag = new Inventory(inventory.RaidSlots, inventory.RaidStackLimits);
            foreach (var stack in rules.Stock(length)) _lines.Add(new ShopLine { Item = stack.Item, Stock = stack.Amount });
        }

        public ShopLine Line(ItemDef item) => _lines.FirstOrDefault(l => l.Item == item);

        public int MinimumFood => _rules.MinimumFood(Length);

        public int Food => _lines.Where(l => l.Item.Type == ItemTypes.Provision).Sum(l => Bag.Count(l.Item));

        /// <summary>The bill.</summary>
        public int Cost => _lines.Sum(l => l.Bought * l.Price);

        /// <summary>Something the party owns already (a class's own item); it is not on the bill and cannot be sold back.</summary>
        public int AddFree(ItemDef item, int amount) => Bag.Add(item, amount);

        /// <summary>How many more units can be bought: what is on the shelf, fits in the bag and the purse covers.</summary>
        public int CanBuy(ItemDef item)
        {
            var line = Line(item);
            if (line == null) return 0;
            var count = Math.Min(line.Left, Bag.Room(item));
            var price = line.Price;
            if (price > 0) count = Math.Min(count, (Purse - Cost) / price);
            return Math.Max(0, count);
        }

        /// <summary>Buys up to <paramref name="amount"/> units; returns how many were bought.</summary>
        public int Buy(ItemDef item, int amount = 1)
        {
            var count = Math.Min(amount, CanBuy(item));
            if (count <= 0) return 0;
            Bag.Add(item, count);
            Line(item).Bought += count;
            return count;
        }

        /// <summary>Puts bought units back on the shelf at the price paid; returns how many went back.</summary>
        public int SellBack(ItemDef item, int amount = 1)
        {
            var line = Line(item);
            if (line == null) return 0;
            var count = Bag.Remove(item, Math.Min(amount, line.Bought));
            line.Bought -= count;
            return count;
        }

        /// <summary>Sells back from one slot of the bag (the stack the player clicked).</summary>
        public int SellBackAt(int slot, int amount = 1)
        {
            var stack = Bag.Slot(slot);
            var line = stack != null ? Line(stack.Item) : null;
            if (line == null) return 0;
            var count = Bag.RemoveAt(slot, Math.Min(amount, line.Bought));
            line.Bought -= count;
            return count;
        }

        /// <summary>
        /// THE MOD'S OWN suggestion, built on DD1's numbers: the food DD1 calls the minimum, a stack of torches
        /// (half a stack more per step of length), a shovel and a key per step of length. Most needed first.
        /// </summary>
        public List<ItemStack> StandardKit()
        {
            var kit = new List<ItemStack>();
            foreach (var want in new[] { ItemTypes.Provision, "torch", "shovel", "skeleton_key" })
            {
                var line = _lines.FirstOrDefault(l => want == ItemTypes.Provision ? l.Item.Type == want : l.Item.Type == ItemTypes.Supply && l.Item.Id == want);
                if (line == null) continue;
                var steps = Math.Max(1, Length);
                var amount = want == ItemTypes.Provision ? Math.Max(MinimumFood, line.Item.StackLimit * 2 / 3)
                    : want == "torch" ? line.Item.StackLimit * (steps + 1) / 2
                    : steps;
                kit.Add(new ItemStack { Item = line.Item, Amount = Math.Min(amount, line.Stock) });
            }
            return kit;
        }

        /// <summary>Tops the bag up to the standard kit as far as the purse goes. Returns true when the whole kit is in.</summary>
        public bool BuyStandardKit()
        {
            var complete = true;
            foreach (var want in StandardKit())
            {
                var missing = want.Amount - Bag.Count(want.Item);
                if (missing > 0 && Buy(want.Item, missing) < missing) complete = false;
            }
            return complete;
        }
    }
}
