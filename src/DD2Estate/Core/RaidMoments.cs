using System.Collections.Generic;

namespace DD2Estate.Core
{
    /// <summary>A hero as DD1's narration looks at them: health in points, stress on DD1's scale of 200.</summary>
    public struct HeroCondition
    {
        public double Health, HealthMax, Stress;
    }

    /// <summary>
    /// When DD1's narration moments of the walk through a dungeon come (<c>audio/narration.json</c>:
    /// enter_hallway, torchlight_out, torchlight_full, half_health_half_stress, hunger, hunger_starve, obstacle,
    /// obstacle_clear_no_item, curio, trap, loot). The files name the moments and their lines; when the game
    /// raises each was read in DD1's executable (the callers of its narration queue, 0x7d6bb0). Which line
    /// answers a moment, with what chance and how often in one expedition, is the file's business (the game
    /// layer's reader of it).
    /// </summary>
    public static class RaidMoments
    {
        /// <summary>DD1's ids of the moments, in the order of its table.</summary>
        public static readonly string[] All =
        {
            "enter_hallway", "torchlight_out", "torchlight_full", "half_health_half_stress", "hunger", "hunger_starve",
            "obstacle", "obstacle_clear_no_item", "curio", "trap", "loot"
        };

        // DD1 behaviour, not in data (constants of the test, exe 0x9b0440): the party's health under this share
        // of its full health, and its stress above this share of 100 a hero (of DD1's 200).
        public const double LowHealthShare = 0.45, HighStressShare = 0.55;
        public const double StressReference = 100;

        /// <summary>"torchlight_full": the torch has just come to its top (any change that brings it there).</summary>
        public static bool TorchFull(double before, double after) => after >= RaidRules.MaxLight && before < RaidRules.MaxLight;

        /// <summary>"torchlight_out": the torch has just gone out.</summary>
        public static bool TorchOut(double before, double after) => after <= 0 && before > 0;

        /// <summary>
        /// "half_health_half_stress", which DD1 looks for whenever a hero gains stress: the party as a whole
        /// (its living heroes added up) has less than 45% of its health and more than 55 stress a hero.
        /// </summary>
        public static bool HalfHealthHalfStress(IEnumerable<HeroCondition> party)
        {
            double health = 0, healthMax = 0, stress = 0;
            var heroes = 0;
            foreach (var hero in party)
            {
                heroes++;
                health += hero.Health;
                healthMax += hero.HealthMax;
                stress += hero.Stress;
            }
            if (heroes == 0 || healthMax <= 0) return false;
            return health / healthMax < LowHealthShare && stress / (StressReference * heroes) > HighStressShare;
        }
    }
}
