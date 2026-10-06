using System;
using System.Collections.Generic;
using System.Linq;
using DD2Estate.Core;

namespace DD2Estate.CoreTests
{
    /// <summary>Never two of the same trinket (Core/TrinketDraws.cs): what a draw may give, whoever asks for it.</summary>
    public static class TrinketDrawTests
    {
        // an estate as the rule sees it: the game's pools by rarity, and how many of each trinket are held
        private sealed class Estate
        {
            public readonly Dictionary<string, List<string>> Pools = new Dictionary<string, List<string>>
            {
                { "common", new List<string> { "c1", "c2", "c3" } },
                { "rare", new List<string> { "r1", "r2" } },
                { "epic", new List<string> { "e1" } },
                { "ancestral", new List<string> { "a1", "a2" } }
            };
            public readonly Dictionary<string, int> Held = new Dictionary<string, int>();
            public bool Unique = true;

            public IEnumerable<string> Pool(string rarity) => Pools.TryGetValue(rarity, out var pool) ? pool : new List<string>();
            public bool MayHold(string id) => TrinketDraws.MayHold(Held.TryGetValue(id, out var n) ? n : 0, Unique);
            public void Take(string id) => Held[id] = (Held.TryGetValue(id, out var n) ? n : 0) + 1;

            public string Draw(string rarity, Rng rng, ICollection<string> taken = null) => TrinketDraws.Draw(rarity, Pool, MayHold, taken, rng, out _);
        }

        public static void OneIsEnough()
        {
            Check.True(TrinketDraws.MayHold(0, true) && !TrinketDraws.MayHold(1, true) && !TrinketDraws.MayHold(2, true), "under the rule the estate takes a trinket it has none of");
            Check.True(TrinketDraws.MayHold(0, false) && TrinketDraws.MayHold(1, false) && TrinketDraws.MayHold(7, false), "with the rule off it takes copies, as in DD1");
        }

        public static void ADrawNeverGivesWhatIsHeld()
        {
            var estate = new Estate();
            var rng = new Rng(11);
            // every source in turn, each trinket taken as it comes: a quest's reward, the wagon, a fight's loot ...
            var given = new List<string>();
            for (var i = 0; i < 40; i++)
            {
                var rarity = new[] { "common", "rare", "epic" }[i % 3];
                var id = estate.Draw(rarity, rng);
                if (id == null) break;
                Check.True(!given.Contains(id), "draw " + i + " gave " + id + " a second time");
                given.Add(id);
                estate.Take(id);
            }
            Check.Equal(6, given.Count, "all six ordinary trinkets were given, each once, and then nothing");
            Check.True(estate.Draw("common", rng) == null && estate.Draw("rare", rng) == null && estate.Draw("epic", rng) == null, "nothing is left to give");
            Check.True(estate.Held.Values.All(n => n == 1), "one of each");
        }

        public static void WhenARarityIsOutTheNearestOtherStandsIn()
        {
            var estate = new Estate();
            var rng = new Rng(3);
            foreach (var id in new[] { "r1", "r2" }) estate.Take(id);
            for (var i = 0; i < 50; i++)
            {
                var id = TrinketDraws.Draw("rare", estate.Pool, estate.MayHold, null, rng, out var from);
                Check.True(id == "e1" && from == "epic", "no rare one left: the rarer neighbour first, got " + id + " from " + from);
            }
            estate.Take("e1");
            for (var i = 0; i < 50; i++)
            {
                var id = TrinketDraws.Draw("rare", estate.Pool, estate.MayHold, null, rng, out var from);
                Check.True(id != null && id.StartsWith("c") && from == "common", "then the cheaper one, got " + id);
            }
            Check.Equal("common, rare, epic", string.Join(", ", TrinketDraws.Nearest("common")), "from the cheapest: upwards");
            Check.Equal("rare, epic, common", string.Join(", ", TrinketDraws.Nearest("rare")), "from the middle: the rarer first");
            Check.Equal("epic, rare, common", string.Join(", ", TrinketDraws.Nearest("epic")), "from the rarest: downwards");
            Check.Equal("", string.Join(", ", TrinketDraws.Nearest("no_such_rarity")), "a rarity the game has not: nothing");
        }

        public static void TheAncestorsTrinketsStandApart()
        {
            var estate = new Estate();
            var rng = new Rng(5);
            foreach (var pool in new[] { "common", "rare", "epic" })
                foreach (var id in estate.Pools[pool]) estate.Take(id);
            Check.True(estate.Draw("epic", rng) == null, "an ordinary draw never reaches for the Ancestor's");
            Check.Equal("ancestral, epic, rare, common", string.Join(", ", TrinketDraws.Nearest("ancestral")), "asked for by name they come first");
            var first = estate.Draw("ancestral", rng);
            Check.True(first == "a1" || first == "a2", "an ancestral draw gives one of them");
            estate.Take(first);
            var second = estate.Draw("ancestral", rng);
            Check.True(second != null && second != first && second.StartsWith("a"), "and then the other");
            estate.Take(second);
            Check.True(estate.Draw("ancestral", rng) == null, "and then nothing: everything is held");
        }

        public static void WhatIsSpokenForIsLeftOutToo()
        {
            var estate = new Estate();
            var rng = new Rng(9);
            // two trinkets of one find, three wares of one week: none twice, though nothing is held yet
            var taken = new List<string>();
            for (var i = 0; i < 3; i++)
            {
                var id = estate.Draw("common", rng, taken);
                Check.True(id != null && !taken.Contains(id), "a third of the same find: " + id);
                taken.Add(id);
            }
            Check.Equal("c1, c2, c3", string.Join(", ", taken.OrderBy(t => t, StringComparer.Ordinal)), "three different ones");
            Check.True(estate.Draw("common", rng, taken).StartsWith("r"), "the fourth comes from the next rarity");
            Check.True(TrinketDraws.From(new[] { "x", "x", null, "y" }, null, new[] { "y" }, rng) == "x", "a pool's repeats and gaps do not matter");
            Check.True(TrinketDraws.From(null, null, null, rng) == null && TrinketDraws.From(new string[0], null, null, rng) == null, "an empty pool gives nothing");
        }

        public static void WithTheRuleOffCopiesDrop()
        {
            var estate = new Estate { Unique = false };
            var rng = new Rng(21);
            var counts = new Dictionary<string, int>();
            for (var i = 0; i < 60; i++)
            {
                var id = estate.Draw("epic", rng);
                Check.Equal("e1", id, "the one epic trinket, again and again");
                estate.Take(id);
            }
            Check.Equal(60, estate.Held["e1"], "sixty of the same");
            for (var i = 0; i < 300; i++)
            {
                var id = estate.Draw("common", rng);
                counts[id] = (counts.TryGetValue(id, out var n) ? n : 0) + 1;
                estate.Take(id);
            }
            Check.True(counts.Count == 3 && counts.Values.All(n => n > 60), "and every common one many times: " + string.Join(", ", counts.Select(p => p.Key + " " + p.Value)));
        }

        public static void ASeedGivesTheSameTrinket()
        {
            var one = new Estate();
            var other = new Estate();
            other.Pools["common"].Reverse();
            other.Pools["common"].Sort(StringComparer.Ordinal);
            for (var seed = 1; seed <= 20; seed++)
                Check.Equal(one.Draw("common", new Rng(seed)), other.Draw("common", new Rng(seed)), "seed " + seed);
        }

        // ---- what is paid when nothing is left, by DD1's own prices ----

        public static void NothingLeftIsPaidAtDd1sSellValue()
        {
            if (!Dd1.Available) throw new SkipException("no DD1 install");
            var rules = TrinketRules.Load(Dd1.Files);
            Check.True(rules.Missing.Count == 0, "DD1's trinket files are read");
            // the price of the rarity less the wagon's share (stock DD1: 85%)
            var previous = 0;
            foreach (var rarity in TrinketRules.Ladder)
            {
                var worth = rules.SellValue(rarity);
                Check.Equal((int)Math.Round(rules.Price(rarity) * (1 - rules.SellDiscount), MidpointRounding.AwayFromZero), worth, rarity + ": its price less the wagon's share");
                Check.True(worth > 0 && worth >= previous, rarity + " is worth " + worth + ", no less than the rarity below");
                previous = worth;
            }
            Check.Equal(0, rules.SellValue("trophy"), "a trophy has no price: nothing is paid in its place");
        }
    }
}
