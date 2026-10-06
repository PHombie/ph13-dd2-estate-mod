using System.Collections.Generic;
using System.Linq;
using DD2Estate.Core;

namespace DD2Estate.CoreTests
{
    /// <summary>DD1's quirk compulsions at curios: the library's numbers, which curio draws whom, DD1's roll and its pick.</summary>
    public static class CompulsionTests
    {
        private static readonly Compulsion Klepto = new Compulsion { Quirk = "kleptomaniac", Tag = "Treasure", Chance = 0.35, KeepLoot = true };
        private static readonly Compulsion Curious = new Compulsion { Quirk = "curious", Tag = "All", Chance = 0.2 };
        private static readonly Compulsion Sure = new Compulsion { Quirk = "sure", Tag = "Treasure", Chance = 1 };
        private static readonly Compulsion Never = new Compulsion { Quirk = "never", Tag = "Treasure", Chance = 0 };

        private static HeroCompulsions Hero(uint guid, params Compulsion[] compulsions)
        {
            return new HeroCompulsions { Hero = guid, Compulsions = compulsions.ToList() };
        }

        public static void TheLibrarysCompulsionsAreRead()
        {
            var rules = CompulsionRules.Load(Dd1.Files);
            var klepto = rules.Of("kleptomaniac");
            Check.True(klepto != null && klepto.Tag == "Treasure" && klepto.KeepLoot, "the kleptomaniac is drawn to treasure and keeps the loot");
            Check.Near(0.35, klepto.Chance, 1e-9, "kleptomaniac curio_tag_chance");
            var temptation = rules.Of("dark_temptation");
            Check.True(temptation != null && temptation.Tag == "Unholy" && !temptation.KeepLoot, "dark temptation is drawn to the unholy");
            Check.Near(0.4, temptation.Chance, 1e-9, "dark_temptation curio_tag_chance");
            var curious = rules.Of("curious");
            Check.True(curious != null && curious.Tag == "All", "the curious are drawn to every curio that lists All");
            Check.Near(0.2, curious.Chance, 1e-9, "curious curio_tag_chance");
            Check.Near(0.6, rules.Of("tapeworm").Chance, 1e-9, "tapeworm curio_tag_chance");
            Check.True(rules.Of("tapeworm").KeepLoot && rules.Of("egomania").KeepLoot, "DD1's other two that keep the loot");
            Check.Equal(3, rules.All.Count(c => c.KeepLoot), "three quirks keep the loot");
            Check.True(rules.Of("calm") == null && rules.Of("no_such_quirk") == null, "a quirk without a curio tag has no compulsion");
            Check.True(CompulsionRules.Load(new NoDd1Files()).All.Count() == 0, "without the install: none");
        }

        public static void ACurioDrawsByItsOwnTags()
        {
            var chest = new List<string> { "All", "Treasure" };
            var altar = new List<string> { "Worship" };       // DD1's altar_of_light lists no "All"
            Check.True(CompulsionRules.Draws(Klepto, chest), "treasure draws the kleptomaniac");
            Check.True(!CompulsionRules.Draws(Klepto, altar), "an altar does not");
            Check.True(CompulsionRules.Draws(Curious, chest), "a curio that lists All draws the curious");
            Check.True(!CompulsionRules.Draws(Curious, altar), "one that does not list it draws nobody by it");
            Check.True(!CompulsionRules.Draws(Klepto, null) && !CompulsionRules.Draws(null, chest), "nothing draws nothing");
        }

        public static void Dd1sCuriosListAllButOne()
        {
            var without = Dd1.Curios.All.Where(c => !c.Tags.Contains("All")).Select(c => c.Id).ToList();
            Check.Equal("altar_of_light", string.Join(", ", without), "the curios that do not list All");
            var chest = Dd1.Curios.Get("heirloom_chest");
            Check.True(chest != null && chest.Tags.Contains("Treasure"), "an heirloom chest is treasure");
        }

        public static void AHerosFirstSuccessStandsForThem()
        {
            var chest = new List<string> { "All", "Treasure" };
            // the first quirk never comes up, the second always: the hero is drawn once, by the second
            var drawn = CompulsionRules.Drawn(new[] { Hero(7, Never, Sure, Klepto) }, chest, new Rng(1));
            Check.True(drawn.Count == 1 && drawn[0].Hero == 7 && drawn[0].Compulsion == Sure, "one entry a hero, by the first quirk that came up");
            // a quirk the curio does not draw is not rolled
            drawn = CompulsionRules.Drawn(new[] { Hero(7, Sure) }, new List<string> { "All", "Food" }, new Rng(1));
            Check.Equal(0, drawn.Count, "treasure-minded at a food curio");
            Check.Equal(0, CompulsionRules.Drawn(null, chest, new Rng(1)).Count, "no party");
        }

        public static void TheChanceIsTheQuirksOwn()
        {
            var chest = new List<string> { "All", "Treasure" };
            const int rolls = 100000;
            var came = 0;
            var rng = new Rng(55);
            var party = new[] { Hero(1, Klepto) };
            for (var i = 0; i < rolls; i++)
                if (CompulsionRules.Roll(party, chest, rng) != null) came++;
            Check.Near(0.35, came / (double)rolls, 0.005, "a kleptomaniac at a chest");
        }

        public static void OfSeveralDrawnTheLastNeverActs()
        {
            // DD1's pick is floor(rand * (count - 1)): kept as it is
            var chest = new List<string> { "Treasure" };
            var one = new List<CompulsionCandidate> { new CompulsionCandidate { Hero = 1, Compulsion = Sure } };
            var rng = new Rng(9);
            for (var i = 0; i < 100; i++) Check.Equal(1u, CompulsionRules.Pick(one, rng).Hero, "one drawn: that one");

            var two = new[] { Hero(1, Sure), Hero(2, Sure) };
            var three = new[] { Hero(1, Sure), Hero(2, Sure), Hero(3, Sure) };
            var picks = new Dictionary<uint, int> { { 1, 0 }, { 2, 0 }, { 3, 0 } };
            for (var i = 0; i < 20000; i++) picks[CompulsionRules.Roll(two, chest, rng).Hero]++;
            Check.True(picks[1] == 20000 && picks[2] == 0, "of two the first always acts");
            picks[1] = 0;
            for (var i = 0; i < 20000; i++) picks[CompulsionRules.Roll(three, chest, rng).Hero]++;
            Check.Equal(0, picks[3], "of three the last never acts");
            Check.Near(0.5, picks[1] / 20000.0, 0.02, "and the other two share");
            Check.True(CompulsionRules.Pick(new List<CompulsionCandidate>(), rng) == null && CompulsionRules.Pick(null, rng) == null, "nobody drawn, nobody acts");
        }
    }
}
