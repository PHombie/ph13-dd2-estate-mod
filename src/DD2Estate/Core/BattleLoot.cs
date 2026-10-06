using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Core
{
    /// <summary>One enemy of a fight as the loot rule sees it: who it is and how many ranks it stands on.</summary>
    public sealed class BattleEnemy
    {
        public string Id = "";
        public int Size = 1;
        /// <summary>
        /// Stands on the enemy's side but need not be beaten for the fight to be won: a barricade, cover, a
        /// corpse. DD1's props and corpses carry no loot code; neither does one of these.
        /// </summary>
        public bool Bystander;
    }

    /// <summary>
    /// DD1's battle loot for fights with another game's monsters.
    ///
    /// DD1 gives every monster a loot code and a count in its own file (<c>monsters/*/*.info.darkest</c>,
    /// <c>loot: .code "A" .count 1</c>): table A once for an ordinary monster, twice for one that stands on
    /// two ranks (ghoul, bone captain, swinetaur, unclean giant, large slime, crab), table T twice for a
    /// quest's boss, nothing for what a boss summons and for the monsters of the Darkest Dungeon. After a won
    /// fight every dead monster's code is drawn, and in the dark the "battle" row of
    /// <c>loot/darkness_overrides.loot.json</c> adds draws of table B.
    ///
    /// DD2's monsters have no such line, so the rule is data of the mod (the <c>battle_loot</c> block of
    /// Data/dungeons.json), read here: one code for an enemy, one for a large enemy (by DD2's own size of the
    /// monster), one for a boss fight, the dungeons whose fights pay nothing, and exceptions by monster id.
    /// The initial values of the fields are DD1's stock numbers, used when the block is missing.
    /// </summary>
    public sealed class BattleLootRules
    {
        /// <summary>The code DD1 writes for a monster that leaves nothing.</summary>
        public const string None = "NONE";

        public LootDraw Enemy = new LootDraw { Table = "A", Draws = 1 };
        public LootDraw LargeEnemy = new LootDraw { Table = "A", Draws = 2 };
        /// <summary>Ranks an enemy stands on from which it counts as large.</summary>
        public int LargeSize = 2;
        public LootDraw Boss = new LootDraw { Table = "T", Draws = 2 };
        /// <summary>The group of DD1's darkness bonuses that a fight draws from.</summary>
        public string DarknessKey = "battle";
        public readonly HashSet<string> NoLootDungeons = new HashSet<string>();
        public readonly Dictionary<string, LootDraw> ByEnemy = new Dictionary<string, LootDraw>();
        /// <summary>
        /// Bosses that pay something of their own in place of <see cref="Boss"/>, by the boss's id: the codes
        /// DD1 writes into that monster's file (the Collector's COLLECTOR, the Shambler's SHAMBLER, B and T).
        /// </summary>
        public readonly Dictionary<string, List<LootDraw>> ByBoss = new Dictionary<string, List<LootDraw>>();

        public static BattleLootRules FromJson(JToken block)
        {
            var rules = new BattleLootRules();
            if (block == null || block.Type != JTokenType.Object) return rules;
            rules.Enemy = Draw(block["enemy"], rules.Enemy);
            rules.LargeEnemy = Draw(block["large_enemy"], rules.LargeEnemy);
            rules.LargeSize = System.Math.Max(2, Json.Int(block["large_enemy"]?["min_size"], rules.LargeSize));
            rules.Boss = Draw(block["boss"], rules.Boss);
            rules.DarknessKey = (string)block["darkness_key"] ?? rules.DarknessKey;
            foreach (var dungeon in Json.Array(block["no_loot_dungeons"]))
                if ((string)dungeon != null) rules.NoLootDungeons.Add((string)dungeon);
            if (block["enemies"] is JObject enemies)
                foreach (var p in enemies.Properties())
                    rules.ByEnemy[p.Name] = Draw(p.Value, new LootDraw { Table = None, Draws = 0 });
            if (block["bosses"] is JObject bosses)
                foreach (var p in bosses.Properties())
                {
                    // one code or a list of them
                    var own = new List<LootDraw>();
                    foreach (var entry in p.Value is JArray several ? (IEnumerable<JToken>)several : new[] { p.Value })
                        own.Add(Draw(entry, new LootDraw { Table = None, Draws = 0 }));
                    rules.ByBoss[p.Name] = own;
                }
            return rules;
        }

        private static LootDraw Draw(JToken token, LootDraw fallback)
        {
            if (token == null || token.Type != JTokenType.Object) return fallback;
            return new LootDraw { Table = (string)token["code"] ?? fallback.Table, Draws = System.Math.Max(0, Json.Int(token["count"], fallback.Draws)) };
        }

        /// <summary>
        /// The table codes a won fight pays, before the dark adds its own: one entry per enemy that leaves
        /// something. In a boss fight the boss pays once (<see cref="Boss"/>, or its own codes when
        /// <paramref name="bossId"/> has some: <see cref="ByBoss"/>) and everything that belongs to it
        /// (<paramref name="bossActors"/>: its parts, what it summons) pays nothing; with no such list the
        /// whole fight is the boss's. Empty for a dungeon whose fights pay nothing.
        /// </summary>
        public List<LootDraw> Draws(string dungeonId, EncounterKind kind, IEnumerable<BattleEnemy> enemies, ICollection<string> bossActors = null, string bossId = null)
        {
            var draws = new List<LootDraw>();
            if (dungeonId != null && NoLootDungeons.Contains(dungeonId)) return draws;
            var boss = kind == EncounterKind.Boss;
            if (boss && bossId != null && ByBoss.TryGetValue(bossId, out var its))
                foreach (var draw in its) Add(draws, draw);
            else if (boss) Add(draws, Boss);
            if (enemies == null) return draws;
            foreach (var enemy in enemies)
            {
                if (enemy == null || enemy.Bystander) continue;
                if (boss && (bossActors == null || bossActors.Count == 0 || bossActors.Contains(enemy.Id))) continue;
                if (ByEnemy.TryGetValue(enemy.Id ?? "", out var own)) Add(draws, own);
                else Add(draws, enemy.Size >= LargeSize ? LargeEnemy : Enemy);
            }
            return draws;
        }

        private static void Add(List<LootDraw> draws, LootDraw draw)
        {
            if (draw == null || draw.Draws <= 0 || string.IsNullOrEmpty(draw.Table) || draw.Table == None) return;
            draws.Add(new LootDraw { Table = draw.Table, Draws = draw.Draws });
        }

        /// <summary>How many times tables are drawn in all.</summary>
        public static int Count(IEnumerable<LootDraw> draws)
        {
            var count = 0;
            if (draws != null)
                foreach (var draw in draws) count += draw.Draws;
            return count;
        }

        /// <summary>
        /// The drops of a won fight: every code drawn from DD1's tables for the quest's difficulty and
        /// dungeon, then the dark's extra codes for the torchlight the fight ended in. A fight that pays
        /// nothing gets nothing from the dark either (GUESS: DD1's files do not say; its monsters without
        /// loot are the ones a boss summons and the Darkest Dungeon's).
        /// </summary>
        public List<LootDrop> Roll(LootTables tables, IEnumerable<LootDraw> draws, double light, int tier, string dungeonId, Rng rng)
        {
            var drops = new List<LootDrop>();
            if (tables == null || draws == null || rng == null) return drops;
            var any = false;
            foreach (var draw in draws)
            {
                if (draw == null || draw.Draws <= 0) continue;
                any = true;
                drops.AddRange(tables.Draw(draw.Table, draw.Draws, tier, dungeonId, rng));
            }
            if (!any) return drops;
            foreach (var code in tables.DarknessBonusCodes(DarknessKey, light, rng))
                drops.AddRange(tables.Draw(code, 1, tier, dungeonId, rng));
            return drops;
        }
    }
}
