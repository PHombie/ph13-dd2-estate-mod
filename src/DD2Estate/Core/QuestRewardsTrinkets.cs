using System;
using System.Collections.Generic;
using System.Linq;

namespace DD2Estate.Core
{
    /// <summary>
    /// DD1's trinket economy, read from the player's install: the rarities
    /// (<c>trinkets/base.rarities.trinkets.json</c>), what a trinket of each rarity costs
    /// (<c>trinkets/base.entries.trinkets.json</c>: every trinket carries its price, and all trinkets of one
    /// rarity carry the same) and the Nomad Wagon's store
    /// (<c>campaign/town/buildings/nomad_wagon/nomad_wagon.building.json</c>: the rarity mix of its stock and
    /// what the estate gets back for a trinket it sells). The trinkets themselves are DD2's: the game layer
    /// maps a DD1 rarity to a DD2 one. The initial values of the fields are FALLBACKS for a missing file or key.
    /// </summary>
    public sealed class TrinketRules
    {
        public const string RaritiesFile = "trinkets/base.rarities.trinkets.json";
        public const string EntriesFile = "trinkets/base.entries.trinkets.json";
        public const string WagonFile = "campaign/town/buildings/nomad_wagon/nomad_wagon.building.json";

        /// <summary>
        /// The rarities quests and the wagon deal in, cheapest first. DD1's other rarities (trophy, crow,
        /// collector...) belong to particular fights and events.
        /// </summary>
        public static readonly string[] Ladder = { "very_common", "common", "uncommon", "rare", "very_rare", "ancestral" };

        /// <summary>Every DD1 rarity with its <c>award_category</c> ("universal", "battle", "trophy"...), in file order.</summary>
        public readonly List<KeyValuePair<string, string>> Rarities = new List<KeyValuePair<string, string>>();

        private readonly Dictionary<string, int> _prices = new Dictionary<string, int>();
        private readonly Dictionary<string, string> _rarityOf = new Dictionary<string, string>();
        private readonly List<KeyValuePair<string, double>> _wagonRarities = new List<KeyValuePair<string, double>>();

        /// <summary>
        /// <c>trinket_sell_value_discount_upgrades</c>: the share of its price a trinket loses when sold (stock
        /// DD1: 0.85, so a 7500 gold trinket sells for 1125).
        /// </summary>
        public double SellDiscount = 0.85;

        /// <summary>Files that could not be read: their rules are the fallbacks.</summary>
        public List<string> Missing = new List<string>();

        public static TrinketRules Load(IDd1Files files)
        {
            var rules = new TrinketRules();
            var rarities = Json.ParseFile(files.ReadText(RaritiesFile));
            foreach (var entry in Json.Array(rarities?["rarities"]))
                if (entry["id"] != null) rules.Rarities.Add(new KeyValuePair<string, string>((string)entry["id"], (string)entry["award_category"] ?? ""));
            if (rules.Rarities.Count == 0) rules.Missing.Add(RaritiesFile);

            var entries = Json.ParseFile(files.ReadText(EntriesFile));
            // the price of a rarity: the one most of its trinkets carry (stock DD1: all of them)
            var counts = new Dictionary<string, Dictionary<int, int>>();
            foreach (var entry in Json.Array(entries?["entries"]))
            {
                var id = (string)entry["id"];
                var rarity = (string)entry["rarity"];
                if (id == null || rarity == null) continue;
                rules._rarityOf[id] = rarity;
                if (!counts.TryGetValue(rarity, out var prices)) counts[rarity] = prices = new Dictionary<int, int>();
                var price = Json.Int(entry["price"], 0);
                prices.TryGetValue(price, out var seen);
                prices[price] = seen + 1;
            }
            foreach (var pair in counts)
                rules._prices[pair.Key] = pair.Value.OrderByDescending(p => p.Value).ThenBy(p => p.Key).First().Key;
            if (counts.Count == 0)
            {
                rules.Missing.Add(EntriesFile);
                // FALLBACK, not read: DD1's stock prices, so that the wagon is not a gift when the file is gone.
                var stock = new[] { 5000, 7500, 10000, 15000, 25000, 50000 };
                for (var i = 0; i < Ladder.Length; i++) rules._prices[Ladder[i]] = stock[i];
            }

            var store = Json.Array(Json.ParseFile(files.ReadText(WagonFile))?.SelectToken("data.stores")).FirstOrDefault()?["data"];
            foreach (var entry in Json.Array(store?["rarity_generation_table"]))
                if (entry["rarity"] != null) rules._wagonRarities.Add(new KeyValuePair<string, double>((string)entry["rarity"], Json.Number(entry["chance"], 0)));
            var sell = Json.Array(store?["trinket_sell_value_discount_upgrades"]).FirstOrDefault();
            if (sell != null) rules.SellDiscount = Math.Max(0, Math.Min(1, Json.Number(sell["discount_percent"], rules.SellDiscount)));
            if (rules._wagonRarities.Count == 0)
            {
                rules.Missing.Add(WagonFile);
                // FALLBACK, not read: an even mix of the rarities the wagon may sell.
                foreach (var rarity in Ladder.Take(5)) rules._wagonRarities.Add(new KeyValuePair<string, double>(rarity, 1));
            }
            return rules;
        }

        /// <summary>DD1 gold a trinket of this rarity costs before any discount; 0 for a rarity DD1 does not sell.</summary>
        public int Price(string rarity) => _prices.TryGetValue(rarity ?? "", out var price) ? price : 0;

        /// <summary>DD1 gold the estate gets for selling a trinket of this rarity.</summary>
        public int SellValue(string rarity) => (int)Math.Round(Price(rarity) * (1 - SellDiscount), MidpointRounding.AwayFromZero);

        /// <summary>The rarity of a DD1 trinket ("boss_necromancer" is a "trophy"); null for an id DD1 does not have.</summary>
        public string RarityOf(string dd1TrinketId) => dd1TrinketId != null && _rarityOf.TryGetValue(dd1TrinketId, out var rarity) ? rarity : null;

        /// <summary>The wagon's <c>rarity_generation_table</c>: rarity and weight.</summary>
        public IReadOnlyList<KeyValuePair<string, double>> WagonRarities => _wagonRarities;

        /// <summary>The rarities of a week's stock, one roll of the wagon's table per trinket.</summary>
        public List<string> RollWagonStock(int count, Rng rng)
        {
            var stock = new List<string>();
            var weights = _wagonRarities.Select(r => r.Value).ToList();
            for (var i = 0; i < count; i++)
            {
                var pick = rng.PickWeighted(weights);
                if (pick < 0) break;
                stock.Add(_wagonRarities[pick].Key);
            }
            return stock;
        }

        /// <summary>Place of a rarity on the <see cref="Ladder"/>; -1 for the special ones.</summary>
        public static int Rank(string rarity) => Array.IndexOf(Ladder, rarity);
    }
}
