using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Core
{
    /// <summary>One row of <c>dismissed_hero_stress_penalties</c>: the stress for a leaver of up to this level.</summary>
    public sealed class DismissalPenalty
    {
        public int UpperLevel;
        public double Stress;
    }

    /// <summary>
    /// DD1's rules of the roster's week, read from the install: what a hero who stayed at home sheds
    /// (<c>campaign/roster/roster.variables.json</c> <c>idle_hero_stress_heal</c>), what a hero below a quest's
    /// level pays for it (<c>shared/rules.json</c> <c>effectiveDifficulty*</c>), and the table of
    /// <c>dismissed_hero_stress_penalties</c> (which DD1's own executables never read: the game layer keeps
    /// that rule switched off). How the numbers are used was read in DD1's executable: the idle heal in
    /// Roster::OnStartTownVisit (0x939860: heroes of the roster who are neither in the party nor in a
    /// building's slot, only when a week has passed), the effective difficulty at 0x84e580, the starting
    /// stress in Party::OnRaidStart (0x97b2f0). Stress is DD1's, on its scale of 200: <see cref="ToDd2"/> and
    /// <see cref="Whole"/> bring it onto a DD2 hero. The initial values of the fields are DD1's stock values,
    /// FALLBACKS for a missing file or key.
    /// </summary>
    public sealed class RosterUpkeepRules
    {
        public const string RulesFile = "shared/rules.json";
        public const string RosterFile = "campaign/roster/roster.variables.json";

        /// <summary>DD1's stress scale.</summary>
        public const double Dd1StressMax = 200;

        /// <summary><c>town_visit_town_progression.idle_hero_stress_heal</c>: a week at home.</summary>
        public double IdleStressHeal = 5;
        /// <summary><c>town_visit_non_town_progression.idle_hero_stress_heal</c>: a visit to the town in which no week passed.</summary>
        public double IdleStressHealNoWeek;

        /// <summary><c>effectiveDifficultyMin</c>, <c>effectiveDifficultyMax</c>.</summary>
        public int EffectiveDifficultyMin, EffectiveDifficultyMax = 6;
        /// <summary><c>effectiveDifficultyDungeonStartingStress</c>, by effective difficulty.</summary>
        public double[] StartingStress = { 0, 20, 30, 40, 50, 60, 70 };
        /// <summary><c>effectiveDifficultyStressDmgModifiers</c>: the share more stress such a hero takes.</summary>
        public double[] StressDamageModifiers = { 0, 0.25, 0.5, 0.75, 1, 1.25, 1.5 };
        /// <summary><c>effectiveDifficultyAfflictionOnsetModifiers</c>.</summary>
        public double[] AfflictionOnsetModifiers = { 0, 0.05, 0.1, 0.15, 0.2, 0.25, 0.33 };
        /// <summary><c>effectiveDifficultyBarkStress</c>.</summary>
        public double BarkStress = 2;

        /// <summary><c>dismissed_hero_stress_penalties</c>, lowest level first.</summary>
        public List<DismissalPenalty> DismissalPenalties = new List<DismissalPenalty>
        {
            new DismissalPenalty { UpperLevel = 4, Stress = 5 }, new DismissalPenalty { UpperLevel = 12, Stress = 10 }, new DismissalPenalty { UpperLevel = 1000000, Stress = 20 }
        };

        public static RosterUpkeepRules Load(IDd1Files files)
        {
            var rules = new RosterUpkeepRules();
            var roster = Json.ParseFile(files.ReadText(RosterFile));
            if (roster != null)
            {
                rules.IdleStressHeal = Json.Number(roster.SelectToken("town_visit_town_progression.idle_hero_stress_heal"), rules.IdleStressHeal);
                rules.IdleStressHealNoWeek = Json.Number(roster.SelectToken("town_visit_non_town_progression.idle_hero_stress_heal"), rules.IdleStressHealNoWeek);
            }
            var raw = Json.ParseFile(files.ReadText(RulesFile));
            if (raw == null) return rules;
            rules.EffectiveDifficultyMin = (int)Math.Round(Json.Number(raw["effectiveDifficultyMin"], rules.EffectiveDifficultyMin));
            rules.EffectiveDifficultyMax = (int)Math.Round(Json.Number(raw["effectiveDifficultyMax"], rules.EffectiveDifficultyMax));
            rules.StartingStress = Numbers(raw["effectiveDifficultyDungeonStartingStress"], rules.StartingStress);
            rules.StressDamageModifiers = Numbers(raw["effectiveDifficultyStressDmgModifiers"], rules.StressDamageModifiers);
            rules.AfflictionOnsetModifiers = Numbers(raw["effectiveDifficultyAfflictionOnsetModifiers"], rules.AfflictionOnsetModifiers);
            rules.BarkStress = Json.Number(raw["effectiveDifficultyBarkStress"], rules.BarkStress);
            var penalties = new List<DismissalPenalty>();
            foreach (var row in Json.Array(raw["dismissed_hero_stress_penalties"]))
                if (row["upper_level"] != null) penalties.Add(new DismissalPenalty { UpperLevel = Json.Int(row["upper_level"], 0), Stress = Json.Number(row["stress_penalty"], 0) });
            if (penalties.Count > 0) rules.DismissalPenalties = penalties.OrderBy(p => p.UpperLevel).ToList();
            return rules;
        }

        private static double[] Numbers(JToken token, double[] fallback)
        {
            var values = Json.Array(token).Select(t => Json.Number(t, 0)).ToArray();
            return values.Length > 0 ? values : fallback;
        }

        // ---- a hero below a quest's level ---------------------------------------------------------------

        // DD1 behaviour, not in data (a constant of Party::OnRaidStart, exe 0x97b2f0): the stress a hero starts
        // a quest with never takes them past this (of 200).
        public const double StartingStressCeiling = 100;

        /// <summary>
        /// A hero's resolve level as DD1 counts it here: the level, and the share of the way to the next one
        /// (a hero at 1 of the 2 experience level 1 asks for is at 0.5); the bare level at the top.
        /// </summary>
        public static double ExactLevel(int level, double shareToNext, int topLevel)
        {
            return level >= topLevel ? level : level + Math.Max(0, Math.Min(1, shareToNext));
        }

        /// <summary>
        /// DD1's "effective difficulty" of a quest for one hero (exe 0x84e580): the quest's difficulty (1, 3, 5,
        /// 6) less the hero's exact level, kept between <see cref="EffectiveDifficultyMin"/> and
        /// <see cref="EffectiveDifficultyMax"/>. A hero fresh off the coach is at 1 in an apprentice's quest;
        /// only at resolve 1 are they at home there.
        /// </summary>
        public double EffectiveDifficulty(double questDifficulty, double exactLevel)
        {
            return Math.Max(EffectiveDifficultyMin, Math.Min(EffectiveDifficultyMax, questDifficulty - exactLevel));
        }

        // DD1 reads its three tables between their rows: 1.5 is half way from row 1 to row 2.
        private static double Lookup(double[] table, double effectiveDifficulty)
        {
            if (table == null || table.Length == 0) return 0;
            var e = Math.Max(0, Math.Min(table.Length - 1, effectiveDifficulty));
            var row = (int)Math.Floor(e);
            if (row >= table.Length - 1) return table[table.Length - 1];
            return table[row] + (table[row + 1] - table[row]) * (e - row);
        }

        /// <summary>The stress (of 200) a hero starts a quest of this effective difficulty with.</summary>
        public double StartingStressAt(double effectiveDifficulty) => Lookup(StartingStress, effectiveDifficulty);

        /// <summary>
        /// What that comes to for a hero who stands at <paramref name="stress"/> (of 200) already: DD1 adds
        /// it and stops at <see cref="StartingStressCeiling"/>; a hero past that gets nothing more.
        /// </summary>
        public double StartingStressFor(double effectiveDifficulty, double stress)
        {
            var after = Math.Min(stress + StartingStressAt(effectiveDifficulty), StartingStressCeiling);
            return Math.Max(0, after - stress);
        }

        /// <summary>The share more stress the hero takes there (0.25: a quarter more).</summary>
        public double StressDamageModifierAt(double effectiveDifficulty) => Lookup(StressDamageModifiers, effectiveDifficulty);

        /// <summary>What is added to the chance that the hero's resolve breaks there.</summary>
        public double AfflictionOnsetModifierAt(double effectiveDifficulty) => Lookup(AfflictionOnsetModifiers, effectiveDifficulty);

        /// <summary>Whether the hero says so as the quest begins (DD1's bark_effective_difficulty): strictly above <see cref="BarkStress"/>.</summary>
        public bool Barks(double effectiveDifficulty) => effectiveDifficulty > BarkStress;

        // ---- a hero is sent away -----------------------------------------------------------------------

        /// <summary>The stress (of 200) of the table's row for a leaver of this level: the first row whose upper level is not below it.</summary>
        public double DismissalStress(int level)
        {
            foreach (var row in DismissalPenalties)
                if (level <= row.UpperLevel) return row.Stress;
            return DismissalPenalties.Count > 0 ? DismissalPenalties[DismissalPenalties.Count - 1].Stress : 0;
        }

        // ---- DD1's stress on a DD2 hero ------------------------------------------------------------------

        /// <summary>DD1 stress (of 200) as the same share of a DD2 hero's scale (10: 20 is one point).</summary>
        public static double ToDd2(double dd1Stress, double dd2StressMax) => dd1Stress * dd2StressMax / Dd1StressMax;

        /// <summary>
        /// DD2 stress moves in whole points and DD1's amounts seldom come to one (5 of 200 is a quarter of a
        /// point): the whole part, and the fraction as the chance of one more, so that the average is DD1's.
        /// </summary>
        public static int Whole(double points, Rng rng)
        {
            if (points <= 0) return 0;
            var whole = Math.Floor(points);
            return (int)whole + (rng.NextDouble() < points - whole ? 1 : 0);
        }
    }
}
