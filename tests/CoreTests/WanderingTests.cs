using System.Collections.Generic;
using DD2Estate.Core;

namespace DD2Estate.CoreTests
{
    /// <summary>DD1's wandering bosses: the conditional mashes of a dungeon, their conditions, limits and dice.</summary>
    public static class WanderingTests
    {
        private const string Crypts3 =
            "hall: .chance 0.08 .types shambler_B    .darkness_range 0 0 .limit 1 .can_be_ambush false\n" +
            "hall: .chance 0.04 .types collector_B    .inventory_valid_item_percent_range 0.79 1.0 .limit 1 .can_be_ambush false\n";

        public static void ALineIsReadWithItsConditions()
        {
            var lines = WanderingRules.Parse(Crypts3);
            Check.Equal(2, lines.Count, "lines");
            var shambler = lines[0];
            Check.Equal("shambler", shambler.Monster, "the monster without DD1's level letter");
            Check.Near(0.08, shambler.Chance, 1e-9, "chance");
            Check.True(shambler.Light != null && shambler.Light[0] == 0 && shambler.Light[1] == 0 && shambler.BagShare == null, "the Shambler asks for the dark, not for a bag");
            Check.True(shambler.Limit == 1 && !shambler.CanBeAmbush, "limit 1, never an ambush");
            var collector = lines[1];
            Check.Equal("collector", collector.Monster, "monster");
            Check.True(collector.BagShare != null && collector.BagShare[0] == 0.79 && collector.BagShare[1] == 1.0 && collector.Light == null, "the Collector asks for a bag, not for the dark");
        }

        public static void ConditionsHoldAtTheirEnds()
        {
            var lines = WanderingRules.Parse(Crypts3);
            Check.True(lines[0].Holds(0, 0), "the Shambler's dark is a torch at 0");
            Check.True(!lines[0].Holds(1, 1), "not at 1");
            Check.True(lines[1].Holds(100, 13 / 16.0), "13 of 16 slots is over 0.79");
            Check.True(!lines[1].Holds(0, 12 / 16.0), "12 of 16 is not");
            Check.True(lines[1].Holds(50, 1.0), "a full bag");
        }

        public static void TheLimitCountsWhatTheExpeditionMet()
        {
            var rules = new WanderingRules();
            rules.Lines.AddRange(WanderingRules.Parse("hall: .chance 1 .types collector_A .inventory_valid_item_percent_range 0.79 1.0 .limit 1"));
            var met = new Dictionary<string, int>();
            Check.Equal("collector", rules.Roll(100, 1, met, new Rng(1))?.Monster, "a chance of 1 always comes");
            met["collector"] = 1;
            Check.True(rules.Roll(100, 1, met, new Rng(1)) == null, "not a second time");
            Check.Near(0, rules.ChanceNow(100, 1, met), 1e-9, "and the chance says so");
            Check.True(rules.Roll(100, 0.5, new Dictionary<string, int>(), new Rng(1)) == null, "not with half a bag");
        }

        public static void TheDiceKeepToTheFilesChances()
        {
            var rules = new WanderingRules();
            rules.Lines.AddRange(WanderingRules.Parse(Crypts3));
            const int rolls = 200000;
            int shambler = 0, collector = 0;
            var rng = new Rng(77);
            var met = new Dictionary<string, int>();
            for (var i = 0; i < rolls; i++)
            {
                // in the dark with a full bag both may come: DD1 makes one roll, each line weighing its chance
                var line = rules.Roll(0, 1, met, rng);
                if (line?.Monster == "shambler") shambler++;
                else if (line?.Monster == "collector") collector++;
            }
            Check.Near(0.08, shambler / (double)rolls, 0.003, "Shambler");
            Check.Near(0.04, collector / (double)rolls, 0.003, "Collector");
            Check.Near(0.12, rules.ChanceNow(0, 1, met), 1e-9, "either of them");
            Check.Near(0.04, rules.ChanceNow(100, 1, met), 1e-9, "in the light only the Collector");
            Check.Near(0, rules.ChanceNow(100, 0.5, met), 1e-9, "in the light with room in the bag nobody");
        }

        public static void LinesThatWeighMoreThanOneShareTheRoll()
        {
            var rules = new WanderingRules();
            rules.Lines.AddRange(WanderingRules.Parse("hall: .chance 0.9 .types first_A\nhall: .chance 0.6 .types second_A\nroom: .chance 1 .types indoors_A"));
            const int rolls = 60000;
            int first = 0, second = 0, none = 0;
            var rng = new Rng(3);
            for (var i = 0; i < rolls; i++)
            {
                var line = rules.Roll(50, 0, null, rng);
                if (line == null) none++;
                else if (line.Monster == "first") first++;
                else if (line.Monster == "second") second++;
            }
            Check.Equal(0, none, "nothing is left of the roll for nobody");
            Check.Near(0.6, first / (double)rolls, 0.01, "0.9 of 1.5");
            Check.Near(0.4, second / (double)rolls, 0.01, "0.6 of 1.5");
            Check.Equal("indoors", rules.Roll(50, 0, null, new Rng(1), "room")?.Monster, "a room's lines are a room's");
        }

        public static void Dd1sFilesGiveEveryRegionBothWanderers()
        {
            var shamblers = new Dictionary<int, double> { { 1, 0.01 }, { 3, 0.08 }, { 5, 0.12 } };
            var collectors = new Dictionary<int, double> { { 1, 0.03 }, { 3, 0.04 }, { 5, 0.05 } };
            foreach (var dungeon in Dd1.Dungeons)
                foreach (var level in new[] { 1, 3, 5 })
                {
                    var rules = WanderingRules.Load(Dd1.Files, dungeon, level);
                    Check.True(rules.Source != null && rules.Lines.Count == 2, dungeon + " " + level + ": two lines");
                    var shambler = rules.Find("shambler");
                    var collector = rules.Find("collector");
                    Check.Near(shamblers[level], shambler.Chance, 1e-9, dungeon + " " + level + ": Shambler");
                    Check.True(shambler.Light[0] == 0 && shambler.Light[1] == 0 && shambler.Limit == 1, dungeon + " " + level + ": in the dark, once");
                    Check.Near(collectors[level], collector.Chance, 1e-9, dungeon + " " + level + ": Collector");
                    Check.True(collector.BagShare[0] == 0.79 && collector.BagShare[1] == 1.0 && collector.Limit == 1, dungeon + " " + level + ": bag at 0.79, once");
                }
            Check.True(WanderingRules.Load(Dd1.Files, "darkestdungeon", 5).Lines.Count == 0, "the Darkest Dungeon has no wanderers");
            Check.True(WanderingRules.Load(new NoDd1Files(), "crypts", 1).Lines.Count == 0, "without the install: none read");
        }
    }
}
