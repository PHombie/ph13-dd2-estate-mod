using DD2Estate.Core;

namespace DD2Estate.CoreTests
{
    /// <summary>DD1's rules of the roster's week: idle relief, a hero below a quest's level, the dismissal table, and DD1's stress on a DD2 hero.</summary>
    public static class RosterUpkeepTests
    {
        public static void TheNumbersAreReadFromDd1()
        {
            var rules = RosterUpkeepRules.Load(Dd1.Files);
            Check.Near(5, rules.IdleStressHeal, 1e-9, "idle_hero_stress_heal, a week passed");
            Check.Near(0, rules.IdleStressHealNoWeek, 1e-9, "idle_hero_stress_heal, no week passed");
            Check.True(rules.EffectiveDifficultyMin == 0 && rules.EffectiveDifficultyMax == 6, "effectiveDifficultyMin / Max");
            Check.Equal("0, 20, 30, 40, 50, 60, 70", string.Join(", ", rules.StartingStress), "effectiveDifficultyDungeonStartingStress");
            Check.Equal("0, 0.25, 0.5, 0.75, 1, 1.25, 1.5", string.Join(", ", System.Array.ConvertAll(rules.StressDamageModifiers, v => v.ToString(System.Globalization.CultureInfo.InvariantCulture))), "effectiveDifficultyStressDmgModifiers");
            Check.Equal("0, 0.05, 0.1, 0.15, 0.2, 0.25, 0.33", string.Join(", ", System.Array.ConvertAll(rules.AfflictionOnsetModifiers, v => v.ToString(System.Globalization.CultureInfo.InvariantCulture))), "effectiveDifficultyAfflictionOnsetModifiers");
            Check.Near(2, rules.BarkStress, 1e-9, "effectiveDifficultyBarkStress");
            Check.Equal(3, rules.DismissalPenalties.Count, "dismissed_hero_stress_penalties rows");
            Check.True(rules.DismissalPenalties[0].UpperLevel == 4 && rules.DismissalPenalties[0].Stress == 5, "row 1");
            Check.True(rules.DismissalPenalties[1].UpperLevel == 12 && rules.DismissalPenalties[1].Stress == 10, "row 2");
            Check.True(rules.DismissalPenalties[2].Stress == 20, "row 3");
        }

        public static void WithoutDd1TheStockValuesStand()
        {
            var rules = RosterUpkeepRules.Load(new NoDd1Files());
            Check.Near(5, rules.IdleStressHeal, 1e-9, "idle heal");
            Check.Near(20, rules.StartingStressAt(1), 1e-9, "starting stress, one above");
            Check.Near(5, rules.DismissalStress(0), 1e-9, "dismissal");
        }

        public static void TheDismissalTableGoesByTheLeaversLevel()
        {
            var rules = new RosterUpkeepRules();
            Check.Near(5, rules.DismissalStress(0), 1e-9, "level 0");
            Check.Near(5, rules.DismissalStress(4), 1e-9, "level 4 is still the first row");
            Check.Near(10, rules.DismissalStress(5), 1e-9, "level 5");
            Check.Near(10, rules.DismissalStress(6), 1e-9, "the top level of a hero");
            Check.Near(20, rules.DismissalStress(13), 1e-9, "past the second row");
        }

        public static void EffectiveDifficultyIsTheQuestsLessTheHerosExactLevel()
        {
            var rules = new RosterUpkeepRules();
            // DD1: a hero fresh off the coach is at 1 in an apprentice's quest, and at home there only at resolve 1
            Check.Near(1, rules.EffectiveDifficulty(1, 0), 1e-9, "level 0, no experience, apprentice");
            Check.Near(0.5, rules.EffectiveDifficulty(1, RosterUpkeepRules.ExactLevel(0, 0.5, 6)), 1e-9, "half way to level 1");
            Check.Near(0, rules.EffectiveDifficulty(1, 1), 1e-9, "level 1, apprentice");
            Check.Near(0, rules.EffectiveDifficulty(1, 4), 1e-9, "never below nothing");
            Check.Near(3, rules.EffectiveDifficulty(3, 0), 1e-9, "level 0, veteran");
            Check.Near(0.25, rules.EffectiveDifficulty(3, 2.75), 1e-9, "level 2 and three quarters, veteran");
            Check.Near(6, rules.EffectiveDifficulty(6, 0), 1e-9, "level 0 in the Darkest Dungeon");
            Check.Near(0, rules.EffectiveDifficulty(6, 6), 1e-9, "level 6 in the Darkest Dungeon");
            Check.Near(1, rules.EffectiveDifficulty(6, 5), 1e-9, "level 5 in the Darkest Dungeon");

            Check.Near(2.5, RosterUpkeepRules.ExactLevel(2, 0.5, 6), 1e-9, "level 2, half way to 3");
            Check.Near(6, RosterUpkeepRules.ExactLevel(6, 1, 6), 1e-9, "the top level has no way further");
            Check.Near(3, RosterUpkeepRules.ExactLevel(3, -1, 6), 1e-9, "a share below nothing is nothing");
        }

        public static void TheTablesAreReadBetweenTheirRows()
        {
            var rules = new RosterUpkeepRules();
            Check.Near(0, rules.StartingStressAt(0), 1e-9, "at home: nothing");
            Check.Near(20, rules.StartingStressAt(1), 1e-9, "one above");
            Check.Near(10, rules.StartingStressAt(0.5), 1e-9, "half above: half way from 0 to 20");
            Check.Near(25, rules.StartingStressAt(1.5), 1e-9, "one and a half: half way from 20 to 30");
            Check.Near(70, rules.StartingStressAt(6), 1e-9, "the last row");
            Check.Near(70, rules.StartingStressAt(9), 1e-9, "beyond the table: its last row");
            Check.Near(0, rules.StartingStressAt(-2), 1e-9, "below it: its first");
            Check.Near(0.5, rules.StressDamageModifierAt(2), 1e-9, "stress taken, two above");
            Check.Near(0.125, rules.StressDamageModifierAt(0.5), 1e-9, "stress taken, half above");
            Check.Near(0.33, rules.AfflictionOnsetModifierAt(6), 1e-9, "affliction onset at the top");
            Check.Near(0.29, rules.AfflictionOnsetModifierAt(5.5), 1e-9, "and half a row below it");
        }

        public static void StartingStressStopsAtAHundred()
        {
            var rules = new RosterUpkeepRules();
            Check.Near(20, rules.StartingStressFor(1, 0), 1e-9, "a calm hero takes it all");
            Check.Near(20, rules.StartingStressFor(1, 80), 1e-9, "80 and 20 are 100");
            Check.Near(10, rules.StartingStressFor(1, 90), 1e-9, "from 90 only up to 100");
            Check.Near(0, rules.StartingStressFor(3, 100), 1e-9, "at 100 nothing more");
            Check.Near(0, rules.StartingStressFor(3, 150), 1e-9, "and nothing is taken back above it");
            Check.Near(0, rules.StartingStressFor(0, 30), 1e-9, "a hero who is up to the quest");
        }

        public static void OnlyAHeroFarOutOfTheirDepthSaysSo()
        {
            var rules = new RosterUpkeepRules();
            Check.True(!rules.Barks(2), "at 2: not yet (DD1 asks for more than 2)");
            Check.True(rules.Barks(2.01), "above 2");
            Check.True(!rules.Barks(1), "at 1");
        }

        public static void Dd1StressLandsOnADd2HeroAtTheSameShare()
        {
            Check.Near(0.25, RosterUpkeepRules.ToDd2(5, 10), 1e-9, "5 of 200 is a quarter of a point of 10");
            Check.Near(1, RosterUpkeepRules.ToDd2(20, 10), 1e-9, "20 of 200 is one point");
            Check.Near(3.5, RosterUpkeepRules.ToDd2(70, 10), 1e-9, "70 of 200");

            Check.Equal(0, RosterUpkeepRules.Whole(0, new Rng(1)), "nothing stays nothing");
            Check.Equal(2, RosterUpkeepRules.Whole(2, new Rng(1)), "whole points are certain");
            const int rolls = 100000;
            var sum = 0;
            var rng = new Rng(5);
            for (var i = 0; i < rolls; i++) sum += RosterUpkeepRules.Whole(0.25, rng);
            Check.Near(0.25, sum / (double)rolls, 0.005, "a quarter of a point is a point one time in four");
            sum = 0;
            for (var i = 0; i < rolls; i++)
            {
                var points = RosterUpkeepRules.Whole(3.5, rng);
                Check.True(points == 3 || points == 4, "3.5 is 3 or 4");
                sum += points;
            }
            Check.Near(3.5, sum / (double)rolls, 0.01, "and 3.5 on average");
        }
    }
}
