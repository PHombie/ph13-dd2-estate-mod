using System.Collections.Generic;
using System.Linq;
using DD2Estate.Core;
using Newtonsoft.Json.Linq;

namespace DD2Estate.CoreTests
{
    /// <summary>The temporary Darkest Dungeon: the setting's three words, the order of the five, which one is on offer, and the quest that is nothing but its fight.</summary>
    public static class DarkestBossTests
    {
        // DD2's own rows (boss_data_export: m_PrerequisiteBossVictoryIds), as the game hands them over.
        private static readonly Dictionary<string, string[]> Game = new Dictionary<string, string[]>
        {
            { "brain", new string[0] }, { "lungs", new[] { "brain" } }, { "eyes", new[] { "lungs" } }, { "arms", new[] { "eyes" } }, { "body", new[] { "arms" } }
        };

        private static readonly string[] Five = { "brain", "lungs", "eyes", "arms", "body" };

        private static IEnumerable<string> Before(string act) => Game.TryGetValue(act, out var before) ? before : new string[0];

        public static void TheSettingHasThreeWords()
        {
            Check.Equal(DarkestMode.Bosses, DarkestBossRules.ParseMode("bosses", out var known), "bosses");
            Check.True(known, "bosses is a word of the setting");
            Check.Equal(DarkestMode.Dd1, DarkestBossRules.ParseMode(" DD1 ", out known), "dd1, however it is typed");
            Check.True(known, "dd1 is a word of the setting");
            Check.Equal(DarkestMode.Shut, DarkestBossRules.ParseMode("Shut", out known), "shut");
            Check.True(known, "shut is a word of the setting");
            Check.Equal(DarkestMode.Bosses, DarkestBossRules.ParseMode("open", out known), "anything else is the default");
            Check.True(!known, "and is told apart from it");
            Check.Equal(DarkestMode.Bosses, DarkestBossRules.ParseMode(null, out known), "nothing at all is the default");
            Check.Equal("bosses", DarkestBossRules.DefaultSetting, "the default");
            foreach (var mode in new[] { DarkestMode.Bosses, DarkestMode.Dd1, DarkestMode.Shut })
                Check.Equal(mode, DarkestBossRules.ParseMode(DarkestBossRules.NameOf(mode), out known), "a mode's word reads back as the mode");
        }

        public static void TheOrderIsTheGamesOwn()
        {
            Check.Equal("brain,lungs,eyes,arms,body", string.Join(",", DarkestBossRules.Order(Five, Before)), "as listed");
            Check.Equal("brain,lungs,eyes,arms,body", string.Join(",", DarkestBossRules.Order(new[] { "body", "eyes", "arms", "brain", "lungs" }, Before)), "from any listing");
            Check.Equal("brain,lungs,eyes,arms,body", string.Join(",", DarkestBossRules.Order(Five.Reverse(), Before)), "from the listing backwards");
            Check.Equal("a,b,c", string.Join(",", DarkestBossRules.Order(new[] { "a", "b", "c" }, act => new string[0])), "where the game says nothing the listing's order stands");
            Check.Equal("a,b,c", string.Join(",", DarkestBossRules.Order(new[] { "a", "b", "c" }, null)), "and so it does without the game");
            Check.Equal("lungs,eyes", string.Join(",", DarkestBossRules.Order(new[] { "eyes", "lungs" }, Before)), "one that waits for something not among them does not wait");
            Check.Equal("z,x,y", string.Join(",", DarkestBossRules.Order(new[] { "x", "y", "z" }, act => act == "x" ? new[] { "y" } : act == "y" ? new[] { "x" } : new string[0])),
                "two that wait for each other come last, and nothing hangs");
            Check.Equal("a,b", string.Join(",", DarkestBossRules.Order(new[] { "a", "", null, "a", "b" }, null)), "each once, and nothing nameless");
            Check.Equal(0, DarkestBossRules.Order(null, Before).Count, "nothing of nothing");
        }

        public static void QuestIdsNameTheirConfession()
        {
            Check.Equal("darkest_boss_brain", DarkestBossRules.QuestId("brain"), "the quest's id");
            Check.Equal("brain", DarkestBossRules.ActOf("darkest_boss_brain"), "and back");
            Check.Equal(null, DarkestBossRules.ActOf("plot_darkest_1"), "a story quest is none of them");
            Check.Equal(null, DarkestBossRules.ActOf("darkest_boss_"), "nor is the bare prefix");
            Check.Equal(null, DarkestBossRules.ActOf(null), "nor nothing");
        }

        public static void OneIsOnOfferFromTheStart()
        {
            var progress = new DarkestBossProgress();
            Check.Equal("brain", progress.Next(Five), "the first is on offer in the first week");
            Check.Equal(0, progress.Count(Five), "nothing is won");
            Check.Equal("Open,Locked,Locked,Locked,Locked", string.Join(",", Five.Select(act => progress.PlaceOf(Five, act))), "one open, four locked");
            Check.Equal(null, progress.Next(new string[0]), "no order, no offer");
            Check.Equal(null, progress.Next(null), "nor without one");
        }

        public static void AWinOpensTheNextAndNeverComesBack()
        {
            var progress = new DarkestBossProgress();
            Check.True(!progress.Win(Five, "lungs"), "a locked one cannot be won");
            Check.True(!progress.Win(Five, "heart"), "nor one that is not of the five");
            Check.True(!progress.Win(Five, null), "nor nothing");
            Check.Equal("brain", progress.Next(Five), "and nothing has moved");

            Check.True(progress.Win(Five, "brain"), "the one on offer is won");
            Check.Equal("lungs", progress.Next(Five), "the next opens");
            Check.Equal("Won,Open,Locked,Locked,Locked", string.Join(",", Five.Select(act => progress.PlaceOf(Five, act))), "won, open, locked");
            Check.True(!progress.Win(Five, "brain"), "a won one is not won again");
            Check.Equal(1, progress.Count(Five), "one of five");

            Check.True(progress.Win(Five, "lungs"), "the second");
            Check.Equal("eyes", progress.Next(Five), "after two wins the third is on offer");
            Check.Equal("Won,Won,Open,Locked,Locked", string.Join(",", Five.Select(act => progress.PlaceOf(Five, act))), "two won");
        }

        public static void AfterTheFifthThereIsNothingMore()
        {
            var progress = new DarkestBossProgress();
            foreach (var act in Five) Check.True(progress.Win(Five, act), act + " in its turn");
            Check.Equal(null, progress.Next(Five), "nothing is on offer");
            Check.Equal(5, progress.Count(Five), "five of five");
            Check.True(Five.All(act => progress.PlaceOf(Five, act) == DarkestBossPlace.Won), "all won");
            Check.True(!progress.Win(Five, "body"), "and none again");
        }

        public static void AQuestNotWonIsThereAgain()
        {
            // a quest lost or abandoned marks nothing: the board asks again next week and gets the same answer
            var progress = new DarkestBossProgress();
            progress.Win(Five, "brain");
            Check.Equal("lungs", progress.Next(Five), "this week");
            Check.Equal("lungs", progress.Next(Five), "and the next");
        }

        public static void ItIsKeptWithTheEstate()
        {
            var progress = new DarkestBossProgress();
            progress.Win(Five, "brain");
            progress.Win(Five, "lungs");
            var saved = progress.ToJson();
            Check.Equal("{\"won\":[\"brain\",\"lungs\"]}", saved.ToString(Newtonsoft.Json.Formatting.None), "the save's section");

            var read = new DarkestBossProgress();
            read.FromJson(JToken.Parse(saved.ToString()));
            Check.Equal("eyes", read.Next(Five), "read back: the third is on offer");
            Check.Equal(2, read.Count(Five), "two won");

            var old = new DarkestBossProgress();
            old.Win(Five, "brain");
            old.FromJson(null);
            Check.Equal("brain", old.Next(Five), "an estate saved before there were any starts at the first");
            old.FromJson(new JArray("brain"));
            Check.Equal("brain", old.Next(Five), "and so does one with something else in the section's place");
            old.FromJson(JToken.Parse("{\"won\":[\"brain\",7,null,\"\",\"heart\"]}"));
            Check.Equal("lungs", old.Next(Five), "what is not a name is passed over");
            Check.Equal(1, old.Count(Five), "and what is not of the five does not count");
        }

        public static void AHoleInASaveIsPlayedFirst()
        {
            var progress = new DarkestBossProgress();
            progress.FromJson(JToken.Parse("{\"won\":[\"brain\",\"eyes\"]}"));
            Check.Equal("lungs", progress.Next(Five), "the first not won is on offer");
            Check.Equal("Won,Open,Won,Locked,Locked", string.Join(",", Five.Select(act => progress.PlaceOf(Five, act))), "the won one behind it stays won");
            Check.True(progress.Win(Five, "lungs"), "it is won");
            Check.Equal("arms", progress.Next(Five), "and the one after the won ones opens");
        }

        public static void TheQuestIsNothingButItsFight()
        {
            var map = DarkestBossRules.FightMap("darkestdungeon", 5, 11);
            Check.True(map.Rooms.Count == 1 && map.Hallways.Count == 0, "one room, no hallway");
            Check.True(map.Rooms[0].Type == RoomType.Boss && map.Rooms[0].Battle.Kind == EncounterKind.Boss && map.EntranceId == 0 && map.FinalRoomId == 0, "the room the party stands in is the boss's");
            Check.True(map.Objective.Kind == ObjectiveKind.KillBoss && map.Objective.Required == 1, "the goal is the boss");
            var again = DungeonMap.FromJson(map.ToJson());
            Check.True(again.Rooms.Count == 1 && again.Rooms[0].Type == RoomType.Boss && again.Rooms[0].Battle != null && again.Objective.Kind == ObjectiveKind.KillBoss, "it comes through a save");
            if (!Dd1.Available) throw new SkipException("the expedition's rules are DD1's files");

            var x = new Exploration(map, Dd1.Raid, Dd1.Curios, 3);
            Check.True(!x.ObjectiveComplete && x.Status == RaidStatus.InProgress, "nothing is done on arrival");
            Check.Equal(1, x.StartFight(null).OfType<FightStarts>().Count(), "the fight begins where the party stands");
            Check.True(!x.CanLeave, "nobody leaves in the middle of it");
            var won = x.ResolveFight(FightOutcome.Won);
            Check.True(won.OfType<ObjectiveCompleted>().Any() && x.ObjectiveComplete && x.IsRoomFightWon(0), "the fight won is the quest's goal");
            Check.True(x.Leave().OfType<QuestEnded>().Any(e => e.Status == RaidStatus.Succeeded), "and the quest ends completed");

            var lost = new Exploration(DarkestBossRules.FightMap("darkestdungeon", 5, 11), Dd1.Raid, Dd1.Curios, 3);
            lost.StartFight(null);
            Check.True(lost.ResolveFight(FightOutcome.Lost).OfType<QuestEnded>().Any(e => e.Status == RaidStatus.Failed), "a party lost has failed it");

            var fled = new Exploration(DarkestBossRules.FightMap("darkestdungeon", 5, 11), Dd1.Raid, Dd1.Curios, 3);
            fled.StartFight(null);
            Check.True(fled.FleeRaid().OfType<QuestEnded>().Any(e => e.Status == RaidStatus.Abandoned), "a party that flees has abandoned it");
        }
    }
}
