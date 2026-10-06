using System.Linq;
using DD2Estate.Core;

namespace DD2Estate.CoreTests
{
    /// <summary>The blacksmith's numbers (Core/GearRules.cs): the owner's rule of 2026-10-06.</summary>
    public static class GearTests
    {
        public static void TheFirstLevelGivesNothing()
        {
            Check.True(GearRules.DamagePercent(1) == 0 && GearRules.CritPercent(1) == 0 && GearRules.HealthPercent(1) == 0, "level 1 is the gear a hero comes with");
            Check.True(GearRules.WeaponWords(1) == null && GearRules.ArmourWords(1) == null, "and there is nothing to say of it");
            Check.Equal(0, GearRules.StatLines(1, 1, "dmg", "crit", "hp").Count, "no stats either");
            Check.True(GearRules.DamagePercent(0) == 0 && GearRules.HealthPercent(-3) == 0 && GearRules.Steps(0) == 0, "a level below the first is the first");
        }

        public static void EveryLevelOverTheFirstAddsTenTenAndOne()
        {
            for (var level = 1; level <= 5; level++)
            {
                Check.Equal((level - 1) * 10, GearRules.DamagePercent(level), "damage at weapon level " + level);
                Check.Equal(level - 1, GearRules.CritPercent(level), "crit at weapon level " + level);
                Check.Equal((level - 1) * 10, GearRules.HealthPercent(level), "health at armour level " + level);
            }
            Check.True(GearRules.DamagePercent(5) == 40 && GearRules.CritPercent(5) == 4 && GearRules.HealthPercent(5) == 40, "level 5: +40% damage, +4% crit, +40% health");
        }

        public static void TheWordsSayWhatTheHeroGets()
        {
            Check.Equal("+10% damage, +1% crit", GearRules.WeaponWords(2), "the first step of a weapon");
            Check.Equal("+40% damage, +4% crit", GearRules.WeaponWords(5), "the last");
            Check.Equal("+10% health", GearRules.ArmourWords(2), "the first step of an armour");
            Check.Equal("+40% health", GearRules.ArmourWords(5), "the last");
            Check.True(!GearRules.WeaponWords(5).Contains("speed") && !GearRules.ArmourWords(5).Contains("taken"), "no speed from a weapon, nothing in place of dodge from an armour");
        }

        public static void TheStatsAreSharesOfOne()
        {
            var lines = GearRules.StatLines(5, 5, "health_damage_dealt_percent", "crit_chance", "health_max");
            Check.Equal("add_stat,health_damage_dealt_percent,0.4 | add_stat,crit_chance,0.04 | multiply_stat,health_max,0.4", string.Join(" | ", lines), "level 5 of both");
            Check.Equal("add_stat,health_damage_dealt_percent,0.3 | add_stat,crit_chance,0.03", string.Join(" | ", GearRules.StatLines(4, 1, "health_damage_dealt_percent", "crit_chance", "health_max")),
                "a weapon alone; and 0.3 is written 0.3");
            Check.Equal("multiply_stat,health_max,0.1", GearRules.StatLines(1, 2, "d", "c", "health_max").Single(), "an armour alone");
            Check.True(GearRules.StatLines(5, 5, "d", "c", "h").All(line => line.Split(',').Length == 3), "three fields a line, as DD2's tables are read");
        }
    }
}
