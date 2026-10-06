using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DD2Estate.Core;
using Newtonsoft.Json.Linq;

namespace DD2Estate.CoreTests
{
    /// <summary>
    /// What a won fight pays (Core/BattleLoot.cs): the rule itself, the mod's data block for it, and both
    /// against DD1's own files (the loot line of every monster, the darkness bonus rows).
    /// </summary>
    public static class BattleLootTests
    {
        private static BattleEnemy Small(string id = "grunt") => new BattleEnemy { Id = id, Size = 1 };
        private static BattleEnemy Large(string id = "brute") => new BattleEnemy { Id = id, Size = 2 };

        // The mod's data file, found from wherever the tests run.
        private static JObject Data()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var path = Path.Combine(dir.FullName, "src", "DD2Estate", "Data", "dungeons.json");
                if (File.Exists(path)) return JObject.Parse(File.ReadAllText(path));
                dir = dir.Parent;
            }
            throw new SkipException("needs the repository's src/DD2Estate/Data/dungeons.json");
        }

        private static JObject DataBlock() => (JObject)Data()["battle_loot"];

        private static string Told(List<LootDraw> draws) => string.Join(" ", draws.Select(d => d.Table + "x" + d.Draws));

        // ---- the rule (no DD1 needed) ----

        public static void EveryEnemyPaysByItsSize()
        {
            var rules = BattleLootRules.FromJson(null);
            Check.Equal("Ax1 Ax1 Ax1", Told(rules.Draws("crypts", EncounterKind.Room, new[] { Small(), Small(), Small() })), "three ordinary monsters in a room");
            Check.Equal("Ax1 Ax2", Told(rules.Draws("weald", EncounterKind.Hallway, new[] { Small(), Large() })), "a large one pays twice");
            Check.Equal("Ax2", Told(rules.Draws("cove", EncounterKind.Hallway, new[] { new BattleEnemy { Id = "huge", Size = 4 } })), "larger still pays as a large one");
            Check.Equal(3, BattleLootRules.Count(rules.Draws("weald", EncounterKind.Hallway, new[] { Small(), Large() })), "draws in all");
            Check.Equal("", Told(rules.Draws("crypts", EncounterKind.Room, new BattleEnemy[0])), "nobody, nothing");
            Check.Equal("", Told(rules.Draws("crypts", EncounterKind.Room, null)), "no list, nothing");
            // cover and corpses stand on the enemy's side and pay nothing
            Check.Equal("Ax1", Told(rules.Draws("weald", EncounterKind.Room, new[] { Small(), new BattleEnemy { Id = "barricade", Bystander = true }, new BattleEnemy { Id = "wall", Size = 2, Bystander = true } })), "a bystander");
            // a hallway fight and a room fight pay alike: DD1's loot hangs on the monster, not on the place
            Check.Equal(Told(rules.Draws("crypts", EncounterKind.Room, new[] { Small(), Large() })), Told(rules.Draws("crypts", EncounterKind.Hallway, new[] { Small(), Large() })), "room and hallway");
        }

        public static void ABossPaysOnceAndItsOwnPayNothing()
        {
            var rules = BattleLootRules.FromJson(null);
            var own = new HashSet<string> { "boss", "boss_arm", "boss_spawn" };
            var fight = new[] { new BattleEnemy { Id = "boss", Size = 2 }, new BattleEnemy { Id = "boss_arm" }, new BattleEnemy { Id = "boss_arm" } };
            Check.Equal("Tx2", Told(rules.Draws("weald", EncounterKind.Boss, fight, own)), "the boss and its parts");
            var guarded = fight.Concat(new[] { Small("guard"), Large("big_guard") });
            Check.Equal("Tx2 Ax1 Ax2", Told(rules.Draws("weald", EncounterKind.Boss, guarded, own)), "a guard that is not the boss's own still pays");
            Check.Equal("Tx2", Told(rules.Draws("weald", EncounterKind.Boss, guarded, null)), "with no list the whole fight is the boss's");
            Check.Equal("Tx2", Told(rules.Draws("weald", EncounterKind.Boss, null, own)), "a boss fight nobody listed");
        }

        public static void ABossWithCodesOfItsOwnPaysThose()
        {
            var rules = BattleLootRules.FromJson(JObject.Parse(@"{
                'boss': {'code': 'T', 'count': 2},
                'bosses': {'hoarder': [{'code': 'H', 'count': 1}, {'code': 'B', 'count': 3}, {'code': 'NONE', 'count': 1}], 'miser': {'code': 'G', 'count': 2}, 'ghost': []}}"));
            var own = new HashSet<string> { "hoarder", "hoarder_chest" };
            var fight = new[] { new BattleEnemy { Id = "hoarder", Size = 2 }, new BattleEnemy { Id = "hoarder_chest" }, Small("guard") };
            Check.Equal("Hx1 Bx3 Ax1", Told(rules.Draws("weald", EncounterKind.Boss, fight, own, "hoarder")), "its own codes in place of the boss's, its parts nothing, a guard as ever");
            Check.Equal("Gx2", Told(rules.Draws("weald", EncounterKind.Boss, null, null, "miser")), "one code written without a list");
            Check.Equal("", Told(rules.Draws("weald", EncounterKind.Boss, null, null, "ghost")), "a boss written to pay nothing");
            Check.Equal("Tx2", Told(rules.Draws("weald", EncounterKind.Boss, null, null, "somebody")), "a boss the map does not name");
            Check.Equal("Tx2", Told(rules.Draws("weald", EncounterKind.Boss, null, null, null)), "no boss named");
            // the map is about boss fights only
            Check.Equal("Ax2 Ax1 Ax1", Told(rules.Draws("weald", EncounterKind.Room, fight, own, "hoarder")), "not a boss fight");
        }

        public static void DataBlockIsRead()
        {
            var rules = BattleLootRules.FromJson(JObject.Parse(@"{
                'enemy': {'code': 'C', 'count': 1}, 'large_enemy': {'code': 'A', 'count': 3, 'min_size': 3}, 'boss': {'code': 'T', 'count': 1},
                'darkness_key': 'chest', 'no_loot_dungeons': ['pit'],
                'enemies': {'spawn': {'code': 'NONE', 'count': 0}, 'hoarder': {'code': 'G', 'count': 2}}}"));
            Check.Equal("Cx1 Cx1 Ax3", Told(rules.Draws("crypts", EncounterKind.Room, new[] { Small(), Large(), new BattleEnemy { Id = "giant", Size = 3 } })), "codes and the size from which one is large");
            Check.Equal("Gx2 Cx1", Told(rules.Draws("crypts", EncounterKind.Room, new[] { Small("hoarder"), Small("spawn"), Small() })), "exceptions by id");
            Check.Equal("Tx1", Told(rules.Draws("crypts", EncounterKind.Boss, null)), "the boss's code");
            Check.Equal("", Told(rules.Draws("pit", EncounterKind.Boss, new[] { Small() })), "a dungeon that pays nothing");
            Check.Equal("chest", rules.DarknessKey, "darkness key");
            // a broken block falls back to DD1's stock numbers
            var stock = BattleLootRules.FromJson(JObject.Parse("{'enemy': 3, 'large_enemy': {'min_size': 0}}"));
            Check.Equal("Ax1 Ax2", Told(stock.Draws("crypts", EncounterKind.Room, new[] { Small(), Large() })), "fallbacks");
        }

        public static void TheModsBlockPaysNothingInTheDarkestDungeon()
        {
            var rules = BattleLootRules.FromJson(DataBlock());
            Check.Equal("", Told(rules.Draws("darkestdungeon", EncounterKind.Room, new[] { Small(), Large() })), "the Darkest Dungeon's fights");
            Check.Equal("", Told(rules.Draws("darkestdungeon", EncounterKind.Boss, new[] { Small() })), "and its bosses");
            Check.Equal("Ax1 Ax2", Told(rules.Draws("warrens", EncounterKind.Room, new[] { Small(), Large() })), "elsewhere DD1's codes");
            Check.Equal("Tx2", Told(rules.Draws("cove", EncounterKind.Boss, new[] { Large("boss") })), "a boss elsewhere");
            Check.Equal("battle", rules.DarknessKey, "DD1's group of darkness bonuses for a fight");
        }

        // ---- against DD1's files ----

        private sealed class Dd1Monster
        {
            public string Name, Code;
            public int Count, Size;
        }

        // Every monster of the base game with its loot line and size, read with nothing but two patterns.
        private static List<Dd1Monster> Monsters()
        {
            if (!Dd1.Available) throw new SkipException("needs the DD1 install");
            var loot = new Regex("^loot:\\s*\\.code\\s*\"([^\"]*)\"\\s*\\.count\\s*(\\d*)", RegexOptions.Multiline);
            var size = new Regex("^display:\\s*\\.size\\s*(\\d+)", RegexOptions.Multiline);
            var monsters = new List<Dd1Monster>();
            foreach (var file in Directory.GetFiles(Path.Combine(Dd1.Root, "monsters"), "*.info.darkest", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(file);
                var l = loot.Match(text);
                var s = size.Match(text);
                if (!l.Success || !s.Success) continue;
                monsters.Add(new Dd1Monster
                {
                    Name = Path.GetFileName(file), Code = l.Groups[1].Value, Size = int.Parse(s.Groups[1].Value),
                    Count = l.Groups[2].Value.Length > 0 ? int.Parse(l.Groups[2].Value) : 0
                });
            }
            Check.True(monsters.Count > 200, "DD1's monster files were not found: " + monsters.Count);
            return monsters;
        }

        private static string Commonest(IEnumerable<Dd1Monster> monsters)
        {
            return monsters.GroupBy(m => m.Code + "x" + m.Count).OrderByDescending(g => g.Count()).First().Key;
        }

        public static void TheModsBlockIsWhatDd1WritesForItsMonsters()
        {
            var monsters = Monsters();
            var rules = BattleLootRules.FromJson(DataBlock());
            // what leaves something, and is not one of DD1's special tables (a boss's own, a DLC's)
            var paying = monsters.Where(m => m.Count > 0 && (m.Code == "A" || m.Code == "C" || m.Code == "T")).ToList();
            Check.Equal(Commonest(paying.Where(m => m.Size == 1 && m.Code != "T")), rules.Enemy.Table + "x" + rules.Enemy.Draws, "an ordinary monster");
            Check.Equal(Commonest(paying.Where(m => m.Size == 2 && m.Code != "T")), rules.LargeEnemy.Table + "x" + rules.LargeEnemy.Draws, "a monster on two ranks");
            Check.Equal(2, rules.LargeSize, "large from two ranks on");
            Check.Equal(Commonest(paying.Where(m => m.Code == "T")), rules.Boss.Table + "x" + rules.Boss.Draws, "a boss");
            // DD1's large monsters pay twice with few exceptions, its ordinary ones once with few
            var large = paying.Where(m => m.Size == 2 && m.Code == "A").ToList();
            Check.True(large.Count(m => m.Count == 2) >= large.Count * 0.7, "most monsters on two ranks pay table A twice: " + large.Count(m => m.Count == 2) + " of " + large.Count);
            var small = paying.Where(m => m.Size == 1 && m.Code == "A").ToList();
            Check.True(small.Count(m => m.Count == 1) >= small.Count * 0.95, "nearly every ordinary monster pays table A once: " + small.Count(m => m.Count == 1) + " of " + small.Count);
            // the Darkest Dungeon's own monsters carry no code
            foreach (var name in new[] { "cultist_orgiastic", "cultist_shrouded", "templar_melee", "ancestor_small", "cyst" })
            {
                var own = monsters.Where(m => m.Name.StartsWith(name + "_", StringComparison.Ordinal)).ToList();
                Check.True(own.Count > 0 && own.All(m => m.Code.Length == 0 || m.Code == BattleLootRules.None), "DD1 gives " + name + " no loot");
            }
            // and the tables the block names exist
            var tables = Dd1.Loot;
            foreach (var code in new[] { rules.Enemy.Table, rules.LargeEnemy.Table, rules.Boss.Table, "B" })
                Check.True(tables.Has(code), "DD1 has no loot table " + code);
        }

        public static void TheWanderersPayWhatDd1WritesForThem()
        {
            if (!Dd1.Available) throw new SkipException("needs the DD1 install");
            var data = Data();
            var rules = BattleLootRules.FromJson((JObject)data["battle_loot"]);
            Check.True(rules.ByBoss.Count > 0, "the block names no boss with codes of its own");
            var tables = Dd1.Loot;
            var line = new Regex("^loot:\\s*\\.code\\s*\"([^\"]*)\"\\s*\\.count\\s*(\\d+)", RegexOptions.Multiline);
            foreach (var boss in rules.ByBoss)
            {
                // the DD1 monster the mod's boss stands for
                var entry = ((JArray)data["wandering"]).FirstOrDefault(w => (string)w["id"] == boss.Key);
                var slot = (string)entry?["dd1_slot"];
                Check.True(slot != null, boss.Key + " is not a wanderer that stands for a DD1 monster");
                var file = Path.Combine(Dd1.Root, "monsters", slot, slot + "_A", slot + "_A.info.darkest");
                Check.True(File.Exists(file), "DD1 has no monster " + slot);
                var dd1 = string.Join(" ", line.Matches(File.ReadAllText(file)).Cast<Match>()
                    .Where(m => m.Groups[1].Value != BattleLootRules.None && int.Parse(m.Groups[2].Value) > 0)
                    .Select(m => m.Groups[1].Value + "x" + m.Groups[2].Value));
                Check.Equal(dd1, Told(rules.Draws("weald", EncounterKind.Boss, null, null, boss.Key)), "the loot lines of DD1's " + slot);
                foreach (var draw in boss.Value) Check.True(tables.Has(draw.Table), "DD1 has no loot table " + draw.Table);
            }

            // the Collector: a trapezohedron three times in four, else one of its own trinkets
            var rng = new Rng(11);
            const int Fights = 2000;
            var collector = rules.Draws("weald", EncounterKind.Boss, null, null, "collector");
            int gems = 0, heads = 0;
            for (var i = 0; i < Fights; i++)
            {
                var drops = rules.Roll(tables, collector, 100, 1, "weald", rng);
                Check.Equal(1, drops.Count, "drops of the Collector");
                if (drops[0].Kind == LootKind.Gem && drops[0].Id == "trapezohedron") gems++;
                else if (drops[0].Kind == LootKind.Trinket && drops[0].Id == "collector") heads++;
            }
            Check.Equal(Fights, gems + heads, "the Collector pays a trapezohedron or a trinket of its own");
            Check.Near(0.75, gems / (double)Fights, 0.04, "the share of trapezohedrons");

            // the Shambler: one of its own trinkets every time, a trinket of table T, and table B three times
            var shambler = rules.Draws("weald", EncounterKind.Boss, null, null, "shambler");
            Check.Equal(5, BattleLootRules.Count(shambler), "draws of the Shambler");
            for (var i = 0; i < 200; i++)
            {
                var drops = rules.Roll(tables, shambler, 100, 3, "weald", rng);
                Check.Equal(1, drops.Count(d => d.Kind == LootKind.Trinket && d.Id == "ancestral_shambler"), "the Shambler's own trinket");
                Check.True(drops.Count(d => d.Kind == LootKind.Trinket) >= 2, "and one of table T: " + string.Join(", ", drops));
            }
        }

        public static void TheDarkAddsDd1sBonusRows()
        {
            var tables = Dd1.Loot;
            var raw = JObject.Parse(Dd1.Files.ReadText("loot/darkness_overrides.loot.json").TrimStart((char)0xFEFF));
            var rows = ((JArray)raw["darkness_bonuses"]).First(g => (string)g["key"] == "battle")["data"];
            var rng = new Rng(2024);
            const int Trials = 4000;
            foreach (var row in rows)
            {
                var light = (double)row["darkness"];
                var chance = (double)row["chance"];
                var codes = ((JArray)row["codes"]).Count;
                var got = 0;
                for (var i = 0; i < Trials; i++) got += tables.DarknessBonusCodes("battle", light, rng).Count;
                Check.Near(chance * codes, got / (double)Trials, 0.05, "extra draws at light " + light + " (DD1: " + chance + " x " + codes + ")");
                // the row holds up to the next one
                if (light < 100) Check.Near(chance * codes, Enumerable.Range(0, Trials).Sum(i => tables.DarknessBonusCodes("battle", light + 0.5, rng).Count) / (double)Trials, 0.05, "just above " + light);
            }
            Check.Equal(0, tables.DarknessBonusCodes("battle", 100, rng).Count, "no bonus in full light");
            Check.Equal(0, tables.DarknessBonusCodes("no_such_key", 0, rng).Count, "an unknown group");
        }

        public static void AFightsLootIsDrawnFromDd1sTables()
        {
            var tables = Dd1.Loot;
            var rules = BattleLootRules.FromJson(DataBlock());
            var fight = rules.Draws("crypts", EncounterKind.Room, new[] { Small(), Small(), Large() });
            Check.Equal(4, BattleLootRules.Count(fight), "two ordinary and a large one");

            // every draw of table A gives one drop (DD1's table has no empty row with a weight)
            int lit = 0, dark = 0;
            var kinds = new HashSet<LootKind>();
            var rng = new Rng(7);
            const int Fights = 600;
            for (var i = 0; i < Fights; i++)
            {
                var drops = rules.Roll(tables, fight, 100, 1, "crypts", rng);
                Check.Equal(4, drops.Count, "drops of a fight in full light");
                lit += drops.Count;
                foreach (var drop in drops) kinds.Add(drop.Kind);
                dark += rules.Roll(tables, fight, 0, 1, "crypts", rng).Count;
            }
            foreach (var kind in new[] { LootKind.Gold, LootKind.Gem, LootKind.Heirloom, LootKind.Supply, LootKind.Trinket })
                Check.True(kinds.Contains(kind), "fights never paid " + kind);
            // DD1 at light 0: three times in four, two draws of table B more
            Check.Near(4 + 0.75 * 2, dark / (double)Fights, 0.15, "drops of a fight in the dark");

            var boss = rules.Roll(tables, rules.Draws("weald", EncounterKind.Boss, new[] { Large("boss") }), 100, 3, "weald", new Rng(3));
            Check.True(boss.Count == 2 && boss.All(d => d.Kind == LootKind.Trinket && d.Id.Length > 0), "a boss pays two trinkets: " + string.Join(", ", boss));

            // a fight that pays nothing gets nothing from the dark either
            var none = rules.Draws("darkestdungeon", EncounterKind.Room, new[] { Small(), Small() });
            for (var i = 0; i < 50; i++) Check.Equal(0, rules.Roll(tables, none, 0, 6, "darkestdungeon", rng).Count, "the Darkest Dungeon in the dark");

            // the same seed, the same loot
            var once = string.Join(",", rules.Roll(tables, fight, 20, 3, "cove", new Rng(99)));
            var again = string.Join(",", rules.Roll(tables, fight, 20, 3, "cove", new Rng(99)));
            Check.Equal(once, again, "a seed gives one result");
            Check.Equal(0, rules.Roll(null, fight, 0, 1, "crypts", rng).Count + rules.Roll(tables, null, 0, 1, "crypts", rng).Count, "nothing to draw from");
        }
    }
}
