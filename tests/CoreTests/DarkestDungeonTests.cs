using System.Collections.Generic;
using System.Linq;
using DD2Estate.Core;

namespace DD2Estate.CoreTests
{
    /// <summary>DD1's rules of the Darkest Dungeon: Never Again, the two experience buffs, and what its plot quests say of themselves.</summary>
    public static class DarkestDungeonTests
    {
        public static void NeverAgainIsReadFromDd1()
        {
            var rules = DarkestDungeonRules.Load(Dd1.Files);
            var strict = rules.Setting("strict");
            Check.True(!strict.CanEnter && strict.BuffIds.Count == 0 && strict.MinimumStress == 0, "strict: may not enter");
            var permissive = rules.Setting("permissive");
            Check.True(permissive.CanEnter && permissive.MinimumStress == 80, "permissive: may enter, at 80 stress or more");
            Check.Equal("never_again_affliction", string.Join(",", permissive.BuffIds), "permissive: its buff");
            Check.Near(-0.25, rules.NeverAgainResolveCheck, 1e-9, "never_again_affliction resolve_check_percent");
            Check.Equal("strict", rules.Setting("no_such_setting").Id, "an unknown setting is the strict one");
            Check.Equal("strict", DarkestDungeonRules.DefaultSetting, "DD1 outside Radiant");
        }

        public static void TheExperienceBuffsAreReadFromDd1()
        {
            var rules = DarkestDungeonRules.Load(Dd1.Files);
            Check.Near(0.5, rules.VeteranXpBonus, 1e-9, "completed_darkest_dungeon_quest_party_resolve_xp");
            Check.Near(1.0, rules.FailureXpBonus, 1e-9, "darkest_dungeon_failure_roster_resolve_xp");
            var stock = DarkestDungeonRules.Load(new NoDd1Files());
            Check.True(stock.VeteranXpBonus == 0.5 && stock.FailureXpBonus == 1 && stock.Setting("permissive").MinimumStress == 80, "without the install: the stock values");
        }

        public static void AVeteranTeachesThoseWhoHaveNotBeenThere()
        {
            var veterans = new HashSet<int> { 2 };
            Check.Equal("1, 3, 4", string.Join(", ", DarkestDungeonRules.Taught(new[] { 1, 2, 3, 4 }, veterans.Contains)), "one veteran: the three others");
            veterans.Add(4);
            Check.Equal("1, 3", string.Join(", ", DarkestDungeonRules.Taught(new[] { 1, 2, 3, 4 }, veterans.Contains)), "two veterans: the two others, once");
            Check.Equal(0, DarkestDungeonRules.Taught(new[] { 1, 3 }, veterans.Contains).Count, "no veteran, no lesson");
            Check.Equal(0, DarkestDungeonRules.Taught(new[] { 2, 4 }, veterans.Contains).Count, "veterans teach each other nothing");
        }

        public static void AFailedDescentAsksForAPartyOfFivesAndUp()
        {
            Check.True(DarkestDungeonRules.PartyQualifies(new[] { 5, 6, 5, 6 }, 5), "every hero at 5 or 6");
            Check.True(!DarkestDungeonRules.PartyQualifies(new[] { 5, 6, 4, 6 }, 5), "one at 4");
            Check.True(DarkestDungeonRules.PartyQualifies(new[] { 6 }, 5), "one hero alone at 6");
            Check.True(!DarkestDungeonRules.PartyQualifies(new int[0], 5), "nobody");
            Check.True(!DarkestDungeonRules.PartyQualifies(null, 5), "no list");
        }

        public static void BonusesAreAddedUpAndRoundedUp()
        {
            Check.Equal(24, DarkestDungeonRules.WithBonus(16, 0.5), "a descent with a veteran");
            Check.Equal(32, DarkestDungeonRules.WithBonus(16, 1.0), "after a failed descent");
            Check.Equal(40, DarkestDungeonRules.WithBonus(16, 1.5), "both");
            Check.Equal(3, DarkestDungeonRules.WithBonus(2, 0.33), "2 at a town event's +33%: 0.66 rounds up to 1");
            Check.Equal(5, DarkestDungeonRules.WithBonus(3, 0.5), "3 at +50%: 1.5 rounds up to 2");
            Check.Equal(12, DarkestDungeonRules.WithBonus(8, 0.5), "a whole share stays whole");
            Check.Equal(8, DarkestDungeonRules.WithBonus(8, 0), "no bonus");
            Check.Equal(0, DarkestDungeonRules.WithBonus(0, 1), "nothing of nothing");
        }

        public static void Dd1sDescentsHaveNoScoutingAndNoSurprise()
        {
            var rewards = QuestRewardRules.Load(Dd1.Files);
            for (var descent = 1; descent <= 4; descent++)
            {
                var plot = rewards.Plot("plot_darkest_dungeon_" + descent);
                Check.True(!plot.SurpriseEnabled && !plot.ScoutingEnabled, "descent " + descent + ": no surprise, no scouting");
                Check.Equal("darkest_dungeon_failure_roster_resolve_xp", string.Join(",", plot.RosterBuffsOnFailure), "descent " + descent + ": the roster's buff on failure");
                Check.Equal(5, plot.RosterBuffMinPartyLevel, "descent " + descent + ": for a party of 5 and up");
                Check.Equal(descent == 4 ? "dd4" : null, plot.TorchSetting, "descent " + descent + ": torch setting");
            }
            var boss = rewards.Plot("plot_kill_necromancer_1");
            Check.True(boss.SurpriseEnabled && boss.ScoutingEnabled && boss.TorchSetting == null && boss.RosterBuffsOnFailure.Count == 0, "a region's boss quest is an ordinary expedition");
        }

        public static void OnlyTheLastDescentsTorchDoesNotBurn()
        {
            var raid = Dd1.Raid;
            Check.True(raid.TorchBurns(null), "no setting: the default burns");
            Check.True(raid.TorchBurns("default"), "default");
            Check.True(!raid.TorchBurns("dd4"), "dd4 does not");
            Check.True(raid.TorchBurns("no_such_setting"), "a setting DD1 does not have burns as the default");
            Check.True(RaidRules.Load(new NoDd1Files()).TorchBurns("dd4"), "without the file every torch burns");
        }

        public static void ATorchThatDoesNotBurnIsMovedByNothing()
        {
            var map = DungeonGenerator.Generate("crypts", QuestTypes.Explore, 1, 1, 4242, Dd1.Files);
            var x = new Exploration(map, Dd1.Raid, Dd1.Curios, 99) { TorchBurns = false };
            var walker = new Explorer(5);
            walker.Run(x, 60);
            Check.Near(100, x.Light, 1e-9, "after a walk");
            Check.Equal(0, x.AddLight(-40).Count, "a curio's darkness");
            Check.Equal(0, x.UseTorch().Count, "a torch");
            Check.Near(100, x.Light, 1e-9, "the light stands");
            Check.True(walker.Log.All(line => !line.StartsWith("LightChanged")), "no change of light was ever told");

            var saved = Exploration.FromJson(x.ToJson(), Dd1.Raid, Dd1.Curios);
            Check.True(!saved.TorchBurns, "the flag is saved");
            var usual = new Exploration(map, Dd1.Raid, Dd1.Curios, 99);
            Check.True(usual.TorchBurns && Exploration.FromJson(usual.ToJson(), Dd1.Raid, Dd1.Curios).TorchBurns, "and is on by default");
        }
    }
}
