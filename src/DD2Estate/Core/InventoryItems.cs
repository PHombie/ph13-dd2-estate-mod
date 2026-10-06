using System;
using System.Collections.Generic;
using System.Linq;

namespace DD2Estate.Core
{
    /// <summary>DD1 item types, spelled as in <c>inventory/*.inventory.items.darkest</c> and the loot tables.</summary>
    public static class ItemTypes
    {
        public const string Gold = "gold";
        public const string Heirloom = "heirloom";
        public const string Gem = "gem";
        public const string Provision = "provision";
        public const string Supply = "supply";
        public const string QuestItem = "quest_item";
        /// <summary>
        /// A trinket found on the way. DD1 carries one in a slot of the raid inventory like anything else, one
        /// to a slot. The id is the trinket's own in the game that owns the trinkets (the game layer's business).
        /// </summary>
        public const string Trinket = "trinket";
    }

    /// <summary>One kind of thing the party can carry. DD1 names an item by type and id; food and gold have no id.</summary>
    public sealed class ItemDef
    {
        public string Type = "", Id = "";
        /// <summary>How many fit in one slot of the raid inventory.</summary>
        public int StackLimit = 1;
        /// <summary>DD1 gold: the price in the provision shop (0: not sold) and what one unit is worth brought home.</summary>
        public int BuyGold, SellGold;
        /// <summary>Pictures DD1 has for a stack as it fills up (food, gold); 0: one picture whatever the amount.</summary>
        public int IconVariants;
        /// <summary>
        /// DD1's <c>shared/inventory/item.display.json</c> (icon_thresholds): the stack size up to which each of
        /// those pictures is shown (gold: 250, 500, 750, 1000; food: 0, 5, 11, 14); null for a type the file
        /// does not name.
        /// </summary>
        public int[] IconThresholds;
        /// <summary><c>can_unpack</c> of the same file: DD1's results screen lays such a stack out a card a unit (food only).</summary>
        public bool CanUnpack;
        /// <summary>The folder of a DD1 feature the item's picture lies under ("dlc/.../features/districts/" for the blueprint); empty for the base game's.</summary>
        public string IconRoot = "";

        /// <summary>"supply:torch", "gem:ruby", "provision", "gold".</summary>
        public string Key => Id.Length > 0 ? Type + ":" + Id : Type;

        /// <summary>The id curios know the item by (<c>curio_type_library.csv</c> item interactions).</summary>
        public string CurioItemId => Type == ItemTypes.Supply ? Id : Type == ItemTypes.Provision ? ItemTypes.Provision : null;

        /// <summary>DD1 icon (72x144) for a stack of this size, relative to the install.</summary>
        public string IconPath(int amount)
        {
            var name = IconRoot + "panels/icons_equip/" + Type + "/inv_" + Type + "+" + Id;
            if (IconVariants <= 0) return name + ".png";
            int index;
            if (IconThresholds != null && IconThresholds.Length > 0)
            {
                // DD1's own steps: the first picture whose threshold the stack does not pass, the last one
                // beyond them all (one ration is picture 1, a purse of more than 750 picture 3).
                index = 0;
                while (index < IconThresholds.Length - 1 && amount > IconThresholds[index]) index++;
            }
            // FALLBACK without the file's steps: the fuller the stack, the later the picture.
            else index = StackLimit <= 0 ? 0 : Math.Max(0, amount) * IconVariants / (StackLimit + 1);
            return name + "_" + Math.Min(IconVariants - 1, index) + ".png";
        }

        public override string ToString() => Key;
    }

    /// <summary>The items of the player's DD1 install (<c>inventory/*.inventory.items.darkest</c>).</summary>
    public sealed class ItemCatalog
    {
        private readonly Dictionary<string, ItemDef> _items = new Dictionary<string, ItemDef>();
        private readonly List<ItemDef> _all = new List<ItemDef>();

        public IReadOnlyList<ItemDef> All => _all;

        public ItemDef Gold => Get(ItemTypes.Gold, "");
        public ItemDef Food => Get(ItemTypes.Provision, "");

        public static ItemCatalog Load(IDd1Files files)
        {
            var catalog = new ItemCatalog();
            foreach (var file in files.List("inventory", "*.inventory.items.darkest").OrderBy(f => f, StringComparer.Ordinal))
                foreach (var block in DarkestFile.Parse(files.ReadText("inventory/" + file)))
                {
                    if (block.Name != "inventory_item") continue;
                    var type = block.Text("type", 0, "");
                    if (type.Length == 0) continue;
                    catalog.Put(new ItemDef
                    {
                        Type = type,
                        Id = block.Text("id", 0, ""),
                        StackLimit = Math.Max(1, block.Int("base_stack_limit", 0, 1)),
                        BuyGold = block.Int("purchase_gold_value", 0, 0),
                        SellGold = block.Int("sell_gold_value", 0, 0)
                    });
                }
            if (catalog._all.Count == 0) catalog.AddFallback();

            foreach (var item in catalog._all)
            {
                var prefix = "inv_" + item.Type + "+" + item.Id + "_";
                item.IconVariants = files.List("panels/icons_equip/" + item.Type, prefix + "*.png").Count(f => IsNumbered(f, prefix.Length));
            }
            catalog.ReadDisplay(files);
            return catalog;
        }

        public const string DisplayFile = "shared/inventory/item.display.json";

        // How a type of item is shown: { "gold": { "icon_thresholds": [...], "can_unpack": false }, "provision": ... }.
        private void ReadDisplay(IDd1Files files)
        {
            var display = Json.ParseFile(files.ReadText(DisplayFile));
            foreach (var item in _all)
            {
                var entry = display?[item.Type];
                if (entry == null || entry.Type != Newtonsoft.Json.Linq.JTokenType.Object)
                {
                    // FALLBACK, used only when the file cannot be read: DD1 unpacks food and nothing else.
                    if (display == null) item.CanUnpack = item.Type == ItemTypes.Provision;
                    continue;
                }
                item.CanUnpack = Json.Bool(entry["can_unpack"], false);
                var steps = Json.Array(entry["icon_thresholds"]).Select(t => Json.Int(t, 0)).ToArray();
                if (steps.Length > 0) item.IconThresholds = steps;
            }
        }

        // "inv_gold+_2.png" is a stack picture; "inv_quest_item+holy_water.png" is not one of an item called "holy"
        private static bool IsNumbered(string file, int prefixLength)
        {
            var dot = file.LastIndexOf('.');
            if (dot <= prefixLength) return false;
            for (var i = prefixLength; i < dot; i++)
                if (!char.IsDigit(file[i])) return false;
            return true;
        }

        // FALLBACK for an install whose inventory files cannot be read: the stock values of
        // base.currency / base.supply.inventory.items.darkest, enough to provision and carry loot.
        private void AddFallback()
        {
            Put(new ItemDef { Type = ItemTypes.Gold, StackLimit = 1750 });
            Put(new ItemDef { Type = ItemTypes.Provision, StackLimit = 12, BuyGold = 75, SellGold = 5 });
            Put(new ItemDef { Type = ItemTypes.Supply, Id = "torch", StackLimit = 8, BuyGold = 75, SellGold = 5 });
            Put(new ItemDef { Type = ItemTypes.Supply, Id = "shovel", StackLimit = 4, BuyGold = 250, SellGold = 25 });
            Put(new ItemDef { Type = ItemTypes.Supply, Id = "skeleton_key", StackLimit = 6, BuyGold = 200, SellGold = 20 });
            Put(new ItemDef { Type = ItemTypes.Supply, Id = "bandage", StackLimit = 6, BuyGold = 150, SellGold = 15 });
            Put(new ItemDef { Type = ItemTypes.Supply, Id = "antivenom", StackLimit = 6, BuyGold = 150, SellGold = 15 });
            Put(new ItemDef { Type = ItemTypes.Supply, Id = "medicinal_herbs", StackLimit = 6, BuyGold = 200, SellGold = 20 });
            Put(new ItemDef { Type = ItemTypes.Supply, Id = "holy_water", StackLimit = 6, BuyGold = 150, SellGold = 15 });
            Put(new ItemDef { Type = ItemTypes.Supply, Id = "laudanum", StackLimit = 6, BuyGold = 100, SellGold = 20 });
            Put(new ItemDef { Type = ItemTypes.Heirloom, Id = "crest", StackLimit = 12 });
            Put(new ItemDef { Type = ItemTypes.Heirloom, Id = "bust", StackLimit = 6 });
            Put(new ItemDef { Type = ItemTypes.Heirloom, Id = "deed", StackLimit = 6 });
            Put(new ItemDef { Type = ItemTypes.Heirloom, Id = "portrait", StackLimit = 3 });
        }

        private void Put(ItemDef item)
        {
            // a later file (mode.*) repeats items of an earlier one
            if (_items.TryGetValue(item.Key, out var old)) _all.Remove(old);
            _items[item.Key] = item;
            _all.Add(item);
        }

        public ItemDef Get(string type, string id)
        {
            return _items.TryGetValue(string.IsNullOrEmpty(id) ? type ?? "" : type + ":" + id, out var item) ? item : null;
        }

        /// <summary>Like <see cref="Get"/>, but an item the files do not list (a DLC gem in a loot table) still gets a slot of its own.</summary>
        public ItemDef Ensure(string type, string id)
        {
            var item = Get(type, id);
            if (item != null) return item;
            item = new ItemDef { Type = type ?? "", Id = id ?? "" };
            Put(item);
            return item;
        }

        /// <summary>A trinket as something the bag carries: one to a slot, worth no gold (it goes to the estate's stores).</summary>
        public ItemDef Trinket(string id) => string.IsNullOrEmpty(id) ? null : Ensure(ItemTypes.Trinket, id);

        /// <summary>
        /// By key ("supply:torch"), by bare id ("torch", "ruby") or by type for the items without an id
        /// ("provision", "gold"). "trinket:&lt;id&gt;" names a trinket, whether the bag has met it before or not.
        /// </summary>
        public ItemDef Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (_items.TryGetValue(name, out var item)) return item;
            if (name.StartsWith(ItemTypes.Trinket + ":", StringComparison.Ordinal)) return Trinket(name.Substring(ItemTypes.Trinket.Length + 1));
            if (name == "food") return Food;
            // supplies first: holy_water and antivenom are also ids of quest items
            return _all.FirstOrDefault(i => i.Id == name && i.Type == ItemTypes.Supply) ?? _all.FirstOrDefault(i => i.Id == name);
        }

        /// <summary>The item behind a curio's item interaction id.</summary>
        public ItemDef ForCurio(string curioItemId)
        {
            if (string.IsNullOrEmpty(curioItemId)) return null;
            return curioItemId == ItemTypes.Provision ? Food : Get(ItemTypes.Supply, curioItemId);
        }

        /// <summary>
        /// The item a loot drop is carried as; null for what the bag does not hold (journal pages, nothing) and
        /// for a trinket, whose drop names a rarity: which trinket it is the game layer draws (<see cref="Trinket"/>).
        /// </summary>
        public ItemDef ForLoot(LootDrop drop)
        {
            switch (drop.Kind)
            {
                case LootKind.Gold: return Ensure(ItemTypes.Gold, "");
                case LootKind.Provision: return Ensure(ItemTypes.Provision, "");
                case LootKind.Heirloom: return Ensure(ItemTypes.Heirloom, drop.Id);
                case LootKind.Gem: return Ensure(ItemTypes.Gem, drop.Id);
                case LootKind.Supply: return Ensure(ItemTypes.Supply, drop.Id);
                default: return null;
            }
        }
    }
}
