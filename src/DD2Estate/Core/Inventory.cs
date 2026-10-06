using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Core
{
    public sealed class ItemStack
    {
        public ItemDef Item;
        public int Amount;

        public override string ToString() => Item + " x" + Amount;
    }

    /// <summary>One stack of the bag that came home.</summary>
    public sealed class HaulStack
    {
        public ItemDef Item;
        /// <summary>What was kept of the stack.</summary>
        public int Amount;
        /// <summary>DD1 gold the estate gets for it: a gold stack its amount, anything else amount x sell value; an heirloom 0.</summary>
        public int Gold;

        public override string ToString() => Item + " x" + Amount + " (" + Gold + ")";
    }

    /// <summary>What an expedition brings home, in DD1 units.</summary>
    public sealed class RaidHaul
    {
        /// <summary>DD1 gold: coins carried, gems and leftover supplies at their sell value.</summary>
        public int Gold;
        public int Coins, GemGold, SupplyGold;
        public readonly Dictionary<string, int> Heirlooms = new Dictionary<string, int>();
        /// <summary>
        /// The trinkets that came home, by id, in the bag's slot order. They go to the estate's trinket storage
        /// and are not among <see cref="Stacks"/>: DD1's results screen has no card for a trinket found.
        /// </summary>
        public readonly List<string> Trinkets = new List<string>();
        /// <summary>The kept stacks in the bag's slot order, each with its worth (DD1's results screen counts them one by one).</summary>
        public readonly List<HaulStack> Stacks = new List<HaulStack>();
    }

    /// <summary>
    /// The party's bag the DD1 way: a fixed number of slots, each holding one stack of one item up to the item's
    /// stack limit. A stack keeps its slot while it lasts; what is picked up tops up the stacks of its kind first
    /// and then takes the first empty slots.
    /// </summary>
    public sealed class Inventory
    {
        private readonly ItemStack[] _slots;

        public int SlotCount => _slots.Length;
        /// <summary>False for DD1's stores and reward lists: one slot takes any amount.</summary>
        public bool UseStackLimits { get; }

        public Inventory(int slots, bool useStackLimits = true)
        {
            _slots = new ItemStack[Math.Max(1, slots)];
            UseStackLimits = useStackLimits;
        }

        /// <summary>The stack in a slot; null when the slot is empty or does not exist.</summary>
        public ItemStack Slot(int index) => index >= 0 && index < _slots.Length ? _slots[index] : null;

        public int FreeSlots
        {
            get
            {
                var free = 0;
                foreach (var stack in _slots)
                    if (stack == null) free++;
                return free;
            }
        }

        public bool IsEmpty => FreeSlots == _slots.Length;

        public int Count(ItemDef item)
        {
            var count = 0;
            foreach (var stack in _slots)
                if (stack != null && stack.Item == item) count += stack.Amount;
            return count;
        }

        private int Limit(ItemDef item) => UseStackLimits ? Math.Max(1, item.StackLimit) : int.MaxValue;

        /// <summary>How many more of an item fit: the space in its stacks plus every empty slot.</summary>
        public int Room(ItemDef item)
        {
            if (item == null) return 0;
            long room = 0;
            var limit = Limit(item);
            foreach (var stack in _slots)
            {
                if (stack == null) room += limit;
                else if (stack.Item == item) room += limit - stack.Amount;
            }
            return (int)Math.Min(int.MaxValue, room);
        }

        /// <summary>Puts items in the bag. Returns what did not fit (0: everything is in).</summary>
        public int Add(ItemDef item, int amount)
        {
            if (item == null || amount <= 0) return 0;
            var limit = Limit(item);
            foreach (var stack in _slots)
            {
                if (stack == null || stack.Item != item || stack.Amount >= limit) continue;
                var take = Math.Min(amount, limit - stack.Amount);
                stack.Amount += take;
                amount -= take;
                if (amount == 0) return 0;
            }
            for (var i = 0; i < _slots.Length && amount > 0; i++)
            {
                if (_slots[i] != null) continue;
                var take = Math.Min(amount, limit);
                _slots[i] = new ItemStack { Item = item, Amount = take };
                amount -= take;
            }
            return amount;
        }

        /// <summary>Takes items out, from the last stack of the kind first so that slots free up. Returns how many were taken.</summary>
        public int Remove(ItemDef item, int amount)
        {
            var taken = 0;
            for (var i = _slots.Length - 1; i >= 0 && taken < amount; i--)
            {
                if (_slots[i] == null || _slots[i].Item != item) continue;
                taken += RemoveAt(i, amount - taken);
            }
            return taken;
        }

        public int RemoveAt(int slot, int amount)
        {
            var stack = Slot(slot);
            if (stack == null || amount <= 0) return 0;
            var take = Math.Min(amount, stack.Amount);
            stack.Amount -= take;
            if (stack.Amount == 0) _slots[slot] = null;
            return take;
        }

        /// <summary>
        /// A stack carried from one slot to another, as a drag in DD1's inventory does it: into an empty slot it
        /// moves, onto a stack of its own kind it tops that one up (what is over the stack limit stays where it
        /// was), onto another kind the two change places. False when nothing changed.
        /// </summary>
        public bool Move(int from, int to)
        {
            var carried = Slot(from);
            if (carried == null || from == to || to < 0 || to >= _slots.Length) return false;
            var there = _slots[to];
            if (there == null)
            {
                _slots[to] = carried;
                _slots[from] = null;
                return true;
            }
            if (there.Item == carried.Item)
            {
                var take = Math.Min(carried.Amount, Limit(there.Item) - there.Amount);
                if (take <= 0)
                {
                    // two full stacks of a kind: they change places, which shows nothing but is no refusal either
                    _slots[to] = carried;
                    _slots[from] = there;
                    return true;
                }
                there.Amount += take;
                carried.Amount -= take;
                if (carried.Amount == 0) _slots[from] = null;
                return true;
            }
            _slots[to] = carried;
            _slots[from] = there;
            return true;
        }

        /// <summary>Empties a slot (dropping a stack to make room) and returns what was in it.</summary>
        public ItemStack Clear(int slot)
        {
            var stack = Slot(slot);
            if (stack != null) _slots[slot] = null;
            return stack;
        }

        /// <summary>
        /// The bag turned into what the estate keeps. <paramref name="keepRate"/> is the share of an item type that
        /// survives (DD1 <c>estate.json</c> quest_fail_keep_rates on a failed quest; 1 for everything on a success).
        /// A trinket is one thing, not a stack to take a share of: a rate between 0 and 1 is its chance of
        /// coming home, rolled with <paramref name="rng"/> (without one such a trinket is lost, as a share of
        /// one rounds down).
        /// </summary>
        public RaidHaul Settle(Func<string, double> keepRate, Rng rng = null)
        {
            var haul = new RaidHaul();
            foreach (var stack in _slots)
            {
                if (stack == null) continue;
                var item = stack.Item;
                var rate = Math.Max(0, Math.Min(1, keepRate != null ? keepRate(item.Type) : 1));
                if (item.Type == ItemTypes.Trinket)
                {
                    for (var i = 0; i < stack.Amount; i++)
                        if (rate >= 1 || (rate > 0 && rng != null && rng.Chance(rate))) haul.Trinkets.Add(item.Id);
                    continue;
                }
                var kept = (int)Math.Floor(stack.Amount * rate + 1e-9);
                if (kept <= 0) continue;
                var worth = 0;
                switch (item.Type)
                {
                    case ItemTypes.Gold:
                        haul.Coins += worth = kept;
                        break;
                    case ItemTypes.Heirloom:
                        haul.Heirlooms.TryGetValue(item.Id, out var have);
                        haul.Heirlooms[item.Id] = have + kept;
                        break;
                    case ItemTypes.Gem:
                        haul.GemGold += worth = kept * item.SellGold;
                        break;
                    case ItemTypes.QuestItem:
                        // stays in the dungeon: nothing of it is listed
                        continue;
                    default:
                        haul.SupplyGold += worth = kept * item.SellGold;
                        break;
                }
                haul.Stacks.Add(new HaulStack { Item = item, Amount = kept, Gold = worth });
            }
            haul.Gold = haul.Coins + haul.GemGold + haul.SupplyGold;
            return haul;
        }

        // ---- save ----

        public JObject ToJson()
        {
            var items = new JArray();
            for (var i = 0; i < _slots.Length; i++)
            {
                var stack = _slots[i];
                if (stack == null) continue;
                items.Add(new JObject { ["slot"] = i, ["type"] = stack.Item.Type, ["id"] = stack.Item.Id, ["n"] = stack.Amount });
            }
            return new JObject { ["slots"] = _slots.Length, ["limits"] = UseStackLimits, ["items"] = items };
        }

        /// <summary>A saved bag. Stacks keep their slots; a stack whose slot is gone or taken goes wherever there is room.</summary>
        public static Inventory FromJson(JToken json, ItemCatalog catalog, int fallbackSlots)
        {
            var bag = new Inventory(Json.Int(json?["slots"], fallbackSlots), Json.Bool(json?["limits"], true));
            if (json == null || catalog == null) return bag;
            foreach (var entry in Json.Array(json["items"]))
            {
                var type = (string)entry["type"];
                var amount = Json.Int(entry["n"], 0);
                if (string.IsNullOrEmpty(type) || amount <= 0) continue;
                var item = catalog.Ensure(type, (string)entry["id"] ?? "");
                var slot = Json.Int(entry["slot"], -1);
                if (slot >= 0 && slot < bag._slots.Length && bag._slots[slot] == null) bag._slots[slot] = new ItemStack { Item = item, Amount = amount };
                else bag.Add(item, amount);
            }
            return bag;
        }
    }
}
