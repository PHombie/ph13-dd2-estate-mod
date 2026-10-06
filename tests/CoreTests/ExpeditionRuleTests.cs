using System.Collections.Generic;
using System.Linq;
using DD2Estate.Core;

namespace DD2Estate.CoreTests
{
    /// <summary>
    /// What the walk tells the game layer for DD1's expedition rules (a fight the party knew of, the first tile
    /// of a hallway, a curio turned back to, a wanderer called up), and when DD1's narration moments come.
    /// </summary>
    public static class ExpeditionRuleTests
    {
        private static readonly RaidRules Rules = RaidRules.Load(new NoDd1Files());

        /// <summary>Room 0 (entrance) - curio, empty, fight - room 1 (a fight) - two empty tiles - room 2. Hand-made: no DD1 files needed.</summary>
        private static DungeonMap Map()
        {
            var map = new DungeonMap { DungeonId = "crypts", QuestType = QuestTypes.Explore, Length = 1, Tier = 3, Width = 3, Height = 1 };
            map.Rooms.Add(new Room { Id = 0, X = 0, Type = RoomType.Entrance });
            map.Rooms.Add(new Room { Id = 1, X = 1, Type = RoomType.Battle, Battle = new EncounterSlot { Kind = EncounterKind.Room, Tier = 3 } });
            map.Rooms.Add(new Room { Id = 2, X = 2, Type = RoomType.Empty });
            var first = new Hallway { Id = 0, RoomA = 0, RoomB = 1 };
            first.Segments.Add(new Segment { Content = HallContent.Curio, PropId = "shamblers_altar" });
            first.Segments.Add(new Segment());
            first.Segments.Add(new Segment { Content = HallContent.Battle, Battle = new EncounterSlot { Kind = EncounterKind.Hallway, Tier = 3 } });
            var second = new Hallway { Id = 1, RoomA = 1, RoomB = 2 };
            second.Segments.Add(new Segment());
            second.Segments.Add(new Segment());
            map.Hallways.Add(first);
            map.Hallways.Add(second);
            map.Objective = new QuestObjective { Kind = ObjectiveKind.Explore, Required = 3 };
            return map;
        }

        private static Exploration Start() => new Exploration(Map(), Rules, null, 1) { ScoutingEnabled = false };

        public static void OnlyAHallwaysFirstTileComesFromARoom()
        {
            var x = Start();
            var first = x.Step(1).OfType<EnteredSegment>().Single();
            Check.True(first.FromRoom && first.Segment == 0, "the first tile out of the entrance");
            var second = x.Step(1).OfType<EnteredSegment>().Single();
            Check.True(!second.FromRoom, "the second tile does not");
            var back = x.Step(0).OfType<EnteredSegment>().Single();
            Check.True(!back.FromRoom && back.BackingUp, "nor a tile walked back to");
        }

        public static void ACurioTurnedBackToIsToldApart()
        {
            var x = Start();
            var found = x.Step(1).OfType<CurioFound>().Single();
            Check.True(!found.Again && found.CurioId == "shamblers_altar", "come to for the first time");
            var again = x.Examine().OfType<CurioFound>().Single();
            Check.True(again.Again, "turned back to");
        }

        public static void AFightIsKnownWhenItsTileWasScoutedOrWalked()
        {
            var x = Start();
            x.Step(1);
            x.Step(1);
            var fight = x.Step(1).OfType<FightStarts>().Single();
            Check.True(!fight.Scouted && fight.Wanderer == null, "walked into unseen");
            // the party flees and comes back: it knows what waits there
            x.ResolveFight(FightOutcome.Retreated);
            fight = x.Step(1).OfType<FightStarts>().Single();
            Check.True(fight.Scouted, "met before");

            var scouted = Start();
            scouted.Scout(0);
            scouted.Step(1);
            scouted.Step(1);
            Check.True(scouted.Step(1).OfType<FightStarts>().Single().Scouted, "scouted");
        }

        public static void AWandererCalledUpIsFoughtWhereThePartyStands()
        {
            var x = Start();
            x.Step(1);
            // the altar's curio is used (no catalogue here: the result is the game layer's), then the summon
            x.UseCurio();
            var events = x.StartFight("shambler");
            var fight = events.OfType<FightStarts>().Single();
            Check.True(fight.Wanderer == "shambler" && fight.Scouted && fight.Slot.Kind == EncounterKind.Hallway && fight.Slot.Tier == 3, "the fight is the wanderer's, known, of the map's tier");
            Check.Equal(PendingKind.Fight, x.Pending, "it has to be answered");
            Check.Equal(0, x.StartFight("shambler").Count, "not twice at once");
            Check.True(!x.CanMoveTo(1), "nobody walks on meanwhile");

            x.ResolveFight(FightOutcome.Won);
            Check.True(x.Pending == PendingKind.None && x.HallwayId == 0 && x.Segment == 0, "won: the party stands where it stood");
            Check.True(x.CanMoveTo(1), "and walks on");

            // fled: back where it came from, as from any fight
            var y = Start();
            y.Step(1);
            y.Step(1);
            y.StartFight("collector");
            y.ResolveFight(FightOutcome.Retreated);
            Check.True(y.HallwayId == 0 && y.Segment == 0 && y.Pending == PendingKind.None, "fled: on the tile before");

            // lost: the expedition is over
            var z = Start();
            z.Step(1);
            z.StartFight("shambler");
            Check.Equal(RaidStatus.Failed, z.ResolveFight(FightOutcome.Lost).OfType<QuestEnded>().Single().Status, "lost");
            Check.Equal(0, z.StartFight("shambler").Count, "nothing is called up after the end");
        }

        public static void ASummonSurvivesTheSave()
        {
            var x = Start();
            x.Step(1);
            x.StartFight("shambler");
            var loaded = Exploration.FromJson(x.ToJson(), Rules, null);
            Check.Equal(PendingKind.Fight, loaded.Pending, "the fight still waits after a load");
            loaded.ResolveFight(FightOutcome.Won);
            Check.Equal(PendingKind.None, loaded.Pending, "and can be answered");
        }

        // ---- DD1's narration moments ----

        public static void TheTorchSpeaksAtItsTopAndWhenItGoesOut()
        {
            Check.True(RaidMoments.TorchFull(75, 100), "lit to the top");
            Check.True(!RaidMoments.TorchFull(100, 100), "not while it stays there");
            Check.True(!RaidMoments.TorchFull(50, 75), "not short of the top");
            Check.True(RaidMoments.TorchOut(6, 0), "gone out");
            Check.True(!RaidMoments.TorchOut(0, 0), "not while it stays out");
            Check.True(!RaidMoments.TorchOut(12, 6), "not while it burns");
            Check.True(!RaidMoments.TorchOut(100, 100) && !RaidMoments.TorchFull(0, 0), "nothing at rest");
        }

        private static HeroCondition Hero(double health, double healthMax, double stress)
        {
            return new HeroCondition { Health = health, HealthMax = healthMax, Stress = stress };
        }

        public static void HalfHealthHalfStressIsThePartysNotAHeros()
        {
            // DD1: the party's health under 45% of its full health and its stress above 55 a hero (of 200)
            var low = new[] { Hero(10, 30, 60), Hero(12, 30, 60), Hero(10, 20, 60), Hero(8, 20, 60) };       // 40 of 100, 60 a hero
            Check.True(RaidMoments.HalfHealthHalfStress(low), "40% health, 60 stress");
            var calm = new[] { Hero(10, 30, 50), Hero(12, 30, 50), Hero(10, 20, 60), Hero(8, 20, 60) };       // 55 a hero: not above
            Check.True(!RaidMoments.HalfHealthHalfStress(calm), "stress at 55 is not above 55");
            var hale = new[] { Hero(15, 30, 90), Hero(12, 30, 90), Hero(10, 20, 90), Hero(8, 20, 90) };       // 45 of 100: not under
            Check.True(!RaidMoments.HalfHealthHalfStress(hale), "health at 45% is not under 45%");
            // one hero in a bad way does not make the party so; one strong hero carries three weak ones
            var one = new[] { Hero(1, 30, 200), Hero(30, 30, 0), Hero(20, 20, 0), Hero(20, 20, 0) };
            Check.True(!RaidMoments.HalfHealthHalfStress(one), "one hero at the end of their rope");
            var carried = new[] { Hero(2, 20, 100), Hero(2, 20, 100), Hero(2, 20, 100), Hero(38, 40, 0) };  // 44 of 100, 75 a hero
            Check.True(RaidMoments.HalfHealthHalfStress(carried), "added up, not hero by hero");
            Check.True(!RaidMoments.HalfHealthHalfStress(new HeroCondition[0]), "nobody left");
            Check.True(RaidMoments.HalfHealthHalfStress(new[] { Hero(4, 10, 56) }), "the last hero standing");
        }

        public static void Dd1sTableHasEveryMomentOfTheWalk()
        {
            var table = Json.ParseFile(Dd1.Files.ReadText("audio/narration.json"));
            var ids = Json.Array(table["entries"]).Select(e => (string)e["id"]).ToList();
            foreach (var moment in RaidMoments.All) Check.True(ids.Contains(moment), "audio/narration.json has no '" + moment + "'");
            Check.Equal(11, RaidMoments.All.Length, "moments of the walk");
        }
    }
}
