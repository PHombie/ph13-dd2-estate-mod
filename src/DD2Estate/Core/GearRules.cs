using System;
using System.Collections.Generic;
using System.Globalization;

namespace DD2Estate.Core
{
    /// <summary>
    /// What the blacksmith's work gives a hero. The owner's numbers (2026-10-06), in place of the growth DD1
    /// writes class by class: every level of the weapon over the first adds a tenth to the damage the hero
    /// deals and one in a hundred to the chance of a crit; every level of the armour over the first adds a
    /// tenth to maximum health. Level 1 is the gear a hero comes with and gives nothing; level 5, DD1's last, is
    /// +40% damage, +4% crit, +40% health. Gear gives nothing else: no speed, and nothing in place of DD1's dodge.
    /// Whole percents, so that what the screen says is what the hero gets.
    /// </summary>
    public static class GearRules
    {
        /// <summary>Percent of damage dealt a weapon level adds.</summary>
        public const int DamagePerLevel = 10;
        /// <summary>Percent of crit chance a weapon level adds.</summary>
        public const int CritPerLevel = 1;
        /// <summary>Percent of maximum health an armour level adds.</summary>
        public const int HealthPerLevel = 10;

        /// <summary>Levels over the first: what the bonuses are counted by.</summary>
        public static int Steps(int level) => Math.Max(0, level - 1);

        /// <summary>Percent more damage dealt at a weapon level.</summary>
        public static int DamagePercent(int weaponLevel) => Steps(weaponLevel) * DamagePerLevel;

        /// <summary>Percent more crit chance at a weapon level.</summary>
        public static int CritPercent(int weaponLevel) => Steps(weaponLevel) * CritPerLevel;

        /// <summary>Percent more maximum health at an armour level.</summary>
        public static int HealthPercent(int armourLevel) => Steps(armourLevel) * HealthPerLevel;

        /// <summary>What a weapon level gives, in words ("+40% damage, +4% crit"); null at the first level.</summary>
        public static string WeaponWords(int level)
        {
            if (Steps(level) == 0) return null;
            return "+" + Whole(DamagePercent(level)) + "% damage, +" + Whole(CritPercent(level)) + "% crit";
        }

        /// <summary>What an armour level gives, in words ("+40% health"); null at the first level.</summary>
        public static string ArmourWords(int level)
        {
            return Steps(level) == 0 ? null : "+" + Whole(HealthPercent(level)) + "% health";
        }

        /// <summary>
        /// The gear as lines of a stats table ("kind,stat,value"): the three stat names are the caller's (the
        /// game's own), the values shares of one. Nothing at the first levels.
        /// </summary>
        public static List<string> StatLines(int weaponLevel, int armourLevel, string damageDealt, string critChance, string healthMax)
        {
            var lines = new List<string>();
            if (DamagePercent(weaponLevel) > 0) lines.Add("add_stat," + damageDealt + "," + Share(DamagePercent(weaponLevel)));
            if (CritPercent(weaponLevel) > 0) lines.Add("add_stat," + critChance + "," + Share(CritPercent(weaponLevel)));
            if (HealthPercent(armourLevel) > 0) lines.Add("multiply_stat," + healthMax + "," + Share(HealthPercent(armourLevel)));
            return lines;
        }

        private static string Whole(int number) => number.ToString(CultureInfo.InvariantCulture);

        // a percent as a share of one, written without a float's noise: 40 is "0.4", 4 is "0.04"
        private static string Share(int percent) => (percent / 100m).ToString("0.####", CultureInfo.InvariantCulture);
    }
}
