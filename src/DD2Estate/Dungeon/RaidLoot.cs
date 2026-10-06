using System.Collections.Generic;
using DD2Estate.Core;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// Where loot comes from, by the names DD1's loot window keys its heading and its line on
    /// (scripts/layout/overlay.loot.darkest: <c>str_overlay_loot_%s_title</c>, <c>_description</c>).
    /// </summary>
    internal enum LootSource
    {
        /// <summary>A curio: "Treasure!" / "Yours for the taking...".</summary>
        Chest,
        /// <summary>A won fight: "Victory!" / "You have found:".</summary>
        Battle,
        /// <summary>A camping skill: "Loot!" / "Available for the taking...".</summary>
        Camping,
        /// <summary>A quest's own find: "Treasure" / "The object of your search!".</summary>
        Quest,
        /// <summary>What a hero who died was wearing: "Reclaimed:" / "From the fallen hero...".</summary>
        HeroDeath,
        /// <summary>DD1's "none": "The Dead's Belongings" / "At least it's something to remember them by...".</summary>
        None
    }

    /// <summary>
    /// Loot lying on DD1's loot scroll: what was found and where. It is the expedition's until every card is
    /// taken or the scroll is closed, and travels in the save meanwhile, so that quitting with the scroll
    /// open loses nothing.
    /// </summary>
    internal sealed class FoundLoot
    {
        public LootSource Source = LootSource.Chest;
        public readonly List<ItemStack> Stacks = new List<ItemStack>();

        public bool Any
        {
            get
            {
                foreach (var stack in Stacks)
                    if (stack != null && stack.Amount > 0) return true;
                return false;
            }
        }

        public static string Key(LootSource source)
        {
            switch (source)
            {
                case LootSource.Battle: return "battle";
                case LootSource.Camping: return "camping";
                case LootSource.Quest: return "quest";
                case LootSource.HeroDeath: return "hero_death";
                case LootSource.None: return "none";
                default: return "chest";
            }
        }

        // DD1's English, for an install whose string table cannot be read.
        private static string StockTitle(LootSource source)
        {
            switch (source)
            {
                case LootSource.Battle: return "Victory!";
                case LootSource.Camping: return "Loot!";
                case LootSource.Quest: return "Treasure";
                case LootSource.HeroDeath: return "Reclaimed:";
                case LootSource.None: return "The Dead's Belongings";
                default: return "Treasure!";
            }
        }

        private static string StockDescription(LootSource source)
        {
            switch (source)
            {
                case LootSource.Battle: return "You have found:";
                case LootSource.Camping: return "Available for the taking...";
                case LootSource.Quest: return "The object of your search!";
                case LootSource.HeroDeath: return "From the fallen hero...";
                case LootSource.None: return "At least it's something to remember them by...";
                default: return "Yours for the taking...";
            }
        }

        /// <summary>The scroll's heading for a source (DD1's str_overlay_loot_&lt;source&gt;_title).</summary>
        public static string Title(LootSource source) => RaidText.Get("str_overlay_loot_" + Key(source) + "_title", StockTitle(source));

        /// <summary>The line under it (str_overlay_loot_&lt;source&gt;_description).</summary>
        public static string Description(LootSource source) => RaidText.Get("str_overlay_loot_" + Key(source) + "_description", StockDescription(source));

        public static LootSource Parse(string key)
        {
            foreach (LootSource source in System.Enum.GetValues(typeof(LootSource)))
                if (Key(source) == key) return source;
            return LootSource.Chest;
        }

        public JObject ToJson()
        {
            var items = new JArray();
            foreach (var stack in Stacks)
                if (stack != null && stack.Item != null && stack.Amount > 0)
                    items.Add(new JObject { ["type"] = stack.Item.Type, ["id"] = stack.Item.Id, ["n"] = stack.Amount });
            return new JObject { ["source"] = Key(Source), ["items"] = items };
        }

        /// <summary>The loot a save was written with on the scroll; null when there was none.</summary>
        public static FoundLoot FromJson(JToken json, ItemCatalog catalog)
        {
            if (json == null || json.Type != JTokenType.Object || catalog == null) return null;
            var loot = new FoundLoot { Source = Parse((string)json["source"]) };
            foreach (var entry in json["items"] as JArray ?? new JArray())
            {
                var type = (string)entry["type"];
                var amount = (int?)entry["n"] ?? 0;
                if (string.IsNullOrEmpty(type) || amount <= 0) continue;
                loot.Stacks.Add(new ItemStack { Item = catalog.Ensure(type, (string)entry["id"] ?? ""), Amount = amount });
            }
            return loot.Any ? loot : null;
        }
    }
}
