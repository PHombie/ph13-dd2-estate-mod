using System;
using System.Collections.Generic;
using System.Linq;
using Assets.Code.Actor;
using DD2Estate.Core;
using DD2Estate.Dev;
using DD2Estate.Dungeon;
using UnityEngine;

namespace DD2Estate.Estate
{
    /// <summary>
    /// The Ancestor on the way through a dungeon: DD1's moments of the walk (<see cref="RaidMoments"/>), raised
    /// by <see cref="DungeonRun"/> where DD1 has them and answered by <see cref="Narration"/> with DD1's
    /// chances, priorities and limits (audio/narration.json: every line of these moments is allowed once an
    /// expedition).
    ///
    ///   moment in the estate                              DD1 trigger                tags given (after the dungeon)
    ///   the party leaves a room for a hallway             enter_hallway
    ///   the torch comes to its top / goes out             torchlight_full / _out
    ///   the hunger question comes up / the party starves  hunger / hunger_starve
    ///   a trap goes off (walked into, or a failed disarm) trap                       the trap
    ///   an obstacle's question comes up                   obstacle                   the obstacle
    ///   an obstacle is dug through by hand                obstacle_clear_no_item     the obstacle
    ///   a curio has given its result                      curio                      the curio
    ///   a curio's loot is laid out                        loot
    ///   a hero gains stress while the party as a whole    half_health_half_stress
    ///   is under 45% health and above 55 stress a hero
    ///
    /// Where DD1 raises each was read in its executable (<see cref="RaidMoments"/>). Not raised here: the
    /// torch line for a torch a wanderer snuffs (DD1 too says nothing while a monster holds the torch) or a
    /// camp relights (DD1 would raise "torchlight_full" there; the camp's own line is left to speak), DD1's
    /// "hunger_starve" for a camp meal skipped (the camp is not this class's), and "loot" for a fight's loot
    /// (DD1 has it for chests only). In a fight nothing is said: the fights have DD2's narrator, so a hero
    /// who gains stress there is looked at when the party is back in the dungeon.
    ///
    /// DD1's lines for its own plot quests (the two of enter_hallway are tagged plot_darkest_dungeon_1 and _2)
    /// stay unsaid, as everywhere in the estate: no moment is given a plot quest's tag. Its Old Road line of
    /// enter_hallway counts as said (Narration.SaidBeforeTheEstate), so that moment is raised and answered by
    /// nobody.
    /// </summary>
    [EstateModule]
    internal static class RaidNarration
    {
        /// <summary>The tags of a moment: the dungeon, then what the moment is about.</summary>
        public static string[] Tags(string dungeonId, params string[] about)
        {
            var tags = new List<string>();
            if (!string.IsNullOrEmpty(dungeonId)) tags.Add(dungeonId);
            foreach (var tag in about ?? new string[0])
                if (!string.IsNullOrEmpty(tag)) tags.Add(tag);
            return tags.ToArray();
        }

        public static bool TorchFull(double before, double after) => RaidMoments.TorchFull(before, after);

        public static bool TorchOut(double before, double after) => RaidMoments.TorchOut(before, after);

        /// <summary>A hero as DD1's test sees them. REMAPPING: the hero's stress at the same share of DD1's scale of 200 (5 of DD2's 10 is DD1's 100).</summary>
        public static HeroCondition ConditionOf(ActorInstance hero)
        {
            return new HeroCondition
            {
                Health = hero.HpRounded,
                HealthMax = hero.CurrentHpMax,
                Stress = Mathf.Clamp01(hero.Stress / Mathf.Max(1f, hero.StressMax)) * RosterUpkeepRules.Dd1StressMax
            };
        }

        public static bool PartyIsLow(IEnumerable<ActorInstance> party)
        {
            return RaidMoments.HalfHealthHalfStress(party.Where(hero => hero != null && hero.IsLiving).Select(ConditionOf));
        }

        /// <summary>
        /// Test commands for the dev bridge:
        ///   narration.raid                        DD1's moments of the walk: chance, lines, how often each was said on this expedition; the party's state
        ///   narration.raid id=trap [about=..]     the moment happens now, as the dungeon would raise it (DD1's dice and limits)
        ///   narration.raid id=trap force=true     the same, the dice and the limits skipped
        /// </summary>
        private static void Register()
        {
            AgentBridge.Register("narration.raid", o =>
            {
                var run = DungeonRun.Current;
                var id = (string)o["id"];
                string said = null;
                if (id != null)
                {
                    if (NarrationRules.Find(id) == null) return "DD1 has no such moment";
                    if (run == null) return "no expedition";
                    var about = ((string)o["about"] ?? "").Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    said = Narration.Answer(id, Narration.Scope.Dungeon, Tags(run.Exploration.Map.DungeonId, about), (bool?)o["force"] ?? false);
                }
                var moments = new List<object>();
                foreach (var moment in RaidMoments.All)
                {
                    var trigger = NarrationRules.Find(moment);
                    if (trigger == null) continue;
                    var tags = run != null ? Tags(run.Exploration.Map.DungeonId) : new string[0];
                    moments.Add(new
                    {
                        id = moment, chance = trigger.Chance,
                        lines = trigger.Lines.Select(l => new { path = l.Path, chance = l.Chance, priority = l.Priority, maxRaid = l.MaxRaid, tags = l.Tags, fits = l.Fits(tags), said = Narration.SaidThisRaid(l.Key) }).ToList()
                    });
                }
                var party = new List<object>();
                if (run != null)
                    foreach (var hero in Provisioning.Party())
                    {
                        var condition = ConditionOf(hero);
                        party.Add(new { guid = hero.ActorGuid, name = hero.ActorName, health = condition.Health, healthMax = condition.HealthMax, stressOf200 = condition.Stress });
                    }
                return new
                {
                    said,
                    light = run?.Exploration.Light,
                    partyIsLow = run != null && PartyIsLow(Provisioning.Party()),
                    party, moments
                };
            });
        }
    }
}
