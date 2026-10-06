using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Assets.Code.Actor;
using DD2Estate.Core;
using DD2Estate.Dd1;
using DD2Estate.Estate;
using UnityEngine;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// A hero's chance to disarm a trap, as DD1 counts it: the class's own skill (the ".trap" of the
    /// resistances line in heroes/&lt;class&gt;/&lt;class&gt;.info.darkest: 10% for a Crusader, 50% for a Grave
    /// Robber), plus shared/rules.json's trap_scout_disarm_bonus (0.4: only a scouted trap can be tried at all),
    /// less the dungeon's difficulty_trap_base. DD1 tells the chance on the hero's tray while the pointer is on
    /// the trap (overlays/tray_trap_disarm.png, resistance_trap_disarm_format "Trap Disarm: %.0f%%").
    ///
    /// DD1's trinkets and quirks move the skill as well; a DD2 hero has no such stat for them to move.
    /// </summary>
    internal static class TrapDisarm
    {
        /// <summary>GUESS: the skill of a class DD1 does not have (the Runaway, the Duelist): DD1's most common value, 8 classes of 15.</summary>
        public const double UnknownClass = 0.10;

        private static readonly Regex Trap = new Regex(@"\.trap\s+(-?[0-9.]+)%");
        private static readonly Dictionary<string, double> Skills = new Dictionary<string, double>();

        /// <summary>The class's own skill, 0..1.</summary>
        public static double Skill(ActorInstance hero)
        {
            var cls = HeroActionUi.ClassOf(hero);
            if (string.IsNullOrEmpty(cls)) return UnknownClass;
            if (Skills.TryGetValue(cls, out var known)) return known;
            var skill = UnknownClass;
            try
            {
                var folder = HeroActionUi.ClassFolder(cls);
                var text = folder != null ? Dd1Install.ReadText(folder + "/" + cls + ".info.darkest") : null;
                var match = text != null ? Trap.Match(text) : Match.Empty;
                if (match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent)) skill = percent / 100.0;
                else Plugin.Log.LogInfo("Trap disarm: DD1 has no skill for the class " + cls + ", " + UnknownClass.ToString("0.##", CultureInfo.InvariantCulture) + " is taken");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Trap disarm: the class " + cls + " could not be read: " + e.Message); }
            Skills[cls] = skill;
            return skill;
        }

        /// <summary>The chance of a try at a scouted trap in a dungeon of this difficulty, 0..1.</summary>
        public static double Chance(ActorInstance hero, RaidRules rules, int tier)
        {
            if (hero == null || rules == null) return 0;
            return Math.Max(0, Math.Min(1, Skill(hero) + rules.TrapScoutDisarmBonus - rules.TrapPenalty(tier)));
        }

        /// <summary>DD1's words for it on a hero's tray.</summary>
        public static string Words(ActorInstance hero, DungeonRun run)
        {
            var chance = run != null ? run.DisarmChance(hero) : 0;
            return RaidText.Format(RaidText.Get("resistance_trap_disarm_format", "{colour_start|trap}Trap{colour_end} Disarm: %.0f%%"), Mathf.RoundToInt((float)(chance * 100)));
        }
    }
}
