using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Core
{
    public enum LootKind { Nothing, Gold, Heirloom, Gem, Provision, Supply, Trinket, JournalPage, Other }

    /// <summary>
    /// One piece of loot in DD1 terms, for the game layer to map: gold amount, heirloom type and count, gem id,
    /// food, supply item id, or a trinket roll of a rarity.
    /// </summary>
    public sealed class LootDrop
    {
        public LootKind Kind;
        /// <summary>Heirloom type, gem or supply id, trinket rarity, or DD1 item type for Other. Empty for gold and food.</summary>
        public string Id = "";
        public int Amount = 1;

        public override string ToString() => Kind + (Id.Length > 0 ? ":" + Id : "") + " x" + Amount;
    }

    /// <summary>DD1 loot tables (<c>loot/loot.json</c> and its override files), drawn the DD1 way: by weight, tables nesting tables.</summary>
    public sealed class LootTables
    {
        private sealed class Table
        {
            public string Id, Dungeon;
            public int Difficulty;
            public readonly List<double> Weights = new List<double>();
            public readonly List<JToken> Entries = new List<JToken>();
        }

        private sealed class DarknessBonus
        {
            public double Light, Chance;
            public List<string> Codes;
        }

        private readonly List<Table> _tables = new List<Table>();
        private readonly Dictionary<string, List<DarknessBonus>> _bonuses = new Dictionary<string, List<DarknessBonus>>();

        public static LootTables Load(IDd1Files files)
        {
            var loot = new LootTables();
            // later files replace tables of earlier ones; A and S exist only in the override file
            foreach (var file in new[] { "loot/loot.json", "loot/common_overrides.loot.json" })
            {
                var json = Json.ParseFile(files.ReadText(file));
                if (json == null) continue;
                foreach (var t in Json.Array(json["loot_tables"]))
                {
                    var table = new Table { Id = (string)t["id"] ?? "", Dungeon = (string)t["dungeon"] ?? "", Difficulty = Json.Int(t["difficulty"], 0) };
                    foreach (var entry in Json.Array(t["entries"]))
                    {
                        table.Weights.Add(Json.Number(entry["chances"], 0));
                        table.Entries.Add(entry);
                    }
                    loot._tables.RemoveAll(o => o.Id == table.Id && o.Dungeon == table.Dungeon && o.Difficulty == table.Difficulty);
                    loot._tables.Add(table);
                }
            }

            loot.UseDarknessBonuses(Json.ParseFile(files.ReadText("loot/darkness_overrides.loot.json"))?["darkness_bonuses"]);
            return loot;
        }

        /// <summary>
        /// Takes the groups of a list written as loot's "darkness_bonuses" ("battle", "chest") in place of the
        /// ones in force; a group the list does not name stays (DD1's Cartographer's Camp carries such a list).
        /// </summary>
        public void UseDarknessBonuses(JToken groups)
        {
            foreach (var group in Json.Array(groups))
            {
                var list = new List<DarknessBonus>();
                foreach (var d in Json.Array(group["data"]))
                    list.Add(new DarknessBonus
                    {
                        Light = Json.Number(d["darkness"], 0),
                        Chance = Json.Number(d["chance"], 0),
                        Codes = Json.Array(d["codes"]).Select(c => (string)c).ToList()
                    });
                _bonuses[(string)group["key"] ?? ""] = list;
            }
        }

        public bool Has(string tableId) => _tables.Any(t => t.Id == tableId);

        /// <summary>Draws from a table. Tier is the DD1 quest difficulty; heirloom tables also depend on the dungeon.</summary>
        public List<LootDrop> Draw(string tableId, int draws, int tier, string dungeon, Rng rng)
        {
            var drops = new List<LootDrop>();
            for (var i = 0; i < draws; i++) DrawOne(tableId, tier, dungeon, rng, drops, 0);
            return drops;
        }

        /// <summary>
        /// Extra table codes the dark adds after a fight ("battle") or to an opened chest ("chest"), rolled for
        /// the current torchlight. Empty most of the time in bright light.
        /// </summary>
        public List<string> DarknessBonusCodes(string key, double light, Rng rng)
        {
            var codes = new List<string>();
            if (!_bonuses.TryGetValue(key, out var list)) return codes;
            DarknessBonus best = null;
            foreach (var bonus in list)
                if (bonus.Light <= light && (best == null || bonus.Light > best.Light)) best = bonus;
            if (best != null && best.Chance > 0 && rng.Chance(best.Chance)) codes.AddRange(best.Codes);
            return codes;
        }

        private Table Find(string id, int tier, string dungeon)
        {
            Table best = null;
            var bestScore = -1;
            foreach (var t in _tables)
            {
                if (t.Id != id) continue;
                if (t.Difficulty != 0 && t.Difficulty != tier) continue;
                if (t.Dungeon.Length > 0 && t.Dungeon != dungeon) continue;
                var score = (t.Difficulty != 0 ? 2 : 0) + (t.Dungeon.Length > 0 ? 1 : 0);
                if (score <= bestScore) continue;
                best = t;
                bestScore = score;
            }
            return best;
        }

        private void DrawOne(string id, int tier, string dungeon, Rng rng, List<LootDrop> drops, int depth)
        {
            var table = Find(id, tier, dungeon);
            if (table == null || depth > 8) return;
            var index = rng.PickWeighted(table.Weights);
            if (index < 0) return;
            var entry = table.Entries[index];
            var data = entry["data"] ?? new JObject();
            switch ((string)entry["type"])
            {
                case "table":
                    DrawOne((string)data["table"], tier, dungeon, rng, drops, depth + 1);
                    break;
                case "trinket":
                    drops.Add(new LootDrop { Kind = LootKind.Trinket, Id = (string)data["rarity"] ?? "" });
                    break;
                case "journal_page":
                    drops.Add(new LootDrop { Kind = LootKind.JournalPage });
                    break;
                case "item":
                    var type = (string)data["type"] ?? "";
                    var kind = type == "gold" ? LootKind.Gold
                        : type == "heirloom" ? LootKind.Heirloom
                        : type == "gem" ? LootKind.Gem
                        : type == "provision" ? LootKind.Provision
                        : type == "supply" ? LootKind.Supply
                        : LootKind.Other;
                    var itemId = (string)data["id"] ?? "";
                    drops.Add(new LootDrop { Kind = kind, Id = kind == LootKind.Other && itemId.Length == 0 ? type : itemId, Amount = Json.Int(data["amount"], 1) });
                    break;
            }
        }
    }
}
