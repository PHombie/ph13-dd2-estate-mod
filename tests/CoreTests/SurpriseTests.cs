using System.Collections.Generic;
using DD2Estate.Core;

namespace DD2Estate.CoreTests
{
    /// <summary>DD1's surprise at the start of a fight: the file's chances, the torchlight's share, the one roll, the monsters' last word.</summary>
    public static class SurpriseTests
    {
        private static LightBand Band(double heroes, double monsters)
        {
            return new LightBand { Values = new Dictionary<string, double> { { "heroes_surprised_increase", heroes }, { "monsters_surprised_increase", monsters } } };
        }

        public static void TheNumbersAreReadFromDd1()
        {
            var rules = SurpriseRules.Load(Dd1.Files);
            Check.Near(0.1, rules.CorridorParty, 1e-9, "surprise_corridor_party_base_chance");
            Check.Near(0.1, rules.CorridorMonsters, 1e-9, "surprise_corridor_monsters_base_chance");
            Check.Near(0.1, rules.RoomParty, 1e-9, "surprise_room_party_base_chance");
            Check.Near(0.1, rules.RoomMonsters, 1e-9, "surprise_room_monsters_base_chance");
            Check.Near(-1, rules.KnownCorridorParty, 1e-9, "surprise_known_corridor_party_base_chance");
            Check.Near(0.25, rules.KnownCorridorMonsters, 1e-9, "surprise_known_corridor_monsters_base_chance");
            Check.Near(-1, rules.KnownRoomParty, 1e-9, "surprise_known_room_party_base_chance");
            Check.Near(0.25, rules.KnownRoomMonsters, 1e-9, "surprise_known_room_monsters_base_chance");
            Check.Near(0.65, rules.MaxParty, 1e-9, "surprise_max_party_surprised_chance");
            Check.Near(0.65, rules.MaxMonsters, 1e-9, "surprise_max_monsters_surprised_chance");
        }

        public static void TorchlightMovesBothChancesAsDd1sTableSays()
        {
            var rules = SurpriseRules.Load(Dd1.Files);
            var raid = Dd1.Raid;
            // light, the party's chance, the monsters' chance in an unknown hallway (base 0.1 each)
            var expected = new[]
            {
                new[] { 100, 0.10, 0.35 }, new[] { 76, 0.10, 0.35 }, new[] { 75, 0.10, 0.25 }, new[] { 51, 0.10, 0.25 },
                new[] { 50, 0.25, 0.20 }, new[] { 26, 0.25, 0.20 }, new[] { 25, 0.35, 0.15 }, new[] { 1, 0.35, 0.15 }, new[] { 0, 0.50, 0.10 }
            };
            foreach (var row in expected)
            {
                var band = raid.BandFor(row[0]);
                Check.Near(row[1], rules.PartyChance(false, false, band, 0), 1e-9, "the party at light " + row[0]);
                Check.Near(row[2], rules.MonstersChance(false, false, band, 0), 1e-9, "the monsters at light " + row[0]);
            }
        }

        public static void AScoutedFightNeverSurprisesTheParty()
        {
            var rules = new SurpriseRules();
            // the darkest band adds 40 points to a base of -1
            Check.Near(0, rules.PartyChance(false, true, Band(40, 0), 0), 1e-9, "a known hallway fight in the dark");
            Check.Near(0, rules.PartyChance(true, true, Band(40, 0), 0), 1e-9, "a known room fight in the dark");
            Check.Near(0.25, rules.MonstersChance(false, true, Band(0, 0), 0), 1e-9, "the monsters of a known fight: 0.25");
            Check.Near(0.50, rules.MonstersChance(true, true, Band(0, 25), 0), 1e-9, "and 0.5 at full light");
            var rng = new Rng(11);
            for (var i = 0; i < 20000; i++)
                Check.True(rules.Roll(i % 2 == 0, true, Band(40, 0), 0, 0, 4, rng) != SurpriseSide.Party, "the party was surprised by a fight it knew of");
        }

        public static void BuffsAddAndBothEndsAreKept()
        {
            var rules = new SurpriseRules();
            Check.Near(0, rules.PartyChance(false, false, Band(0, 0), -0.2), 1e-9, "a camping skill takes the party's chance below nothing: nothing");
            Check.Near(0.65, rules.PartyChance(false, false, Band(40, 0), 0.3), 1e-9, "never above the maximum");
            Check.Near(0.65, rules.MonstersChance(false, true, Band(0, 25), 0.3), 1e-9, "the monsters' maximum");
            Check.Near(0.3, rules.MonstersChance(false, false, Band(0, 0), 0.2), 1e-9, "a buff of 0.2 on a base of 0.1");
        }

        public static void APartyOfOneStartsFromNothing()
        {
            var rules = new SurpriseRules();
            Check.Near(0, rules.PartyChance(false, false, Band(0, 0), 0, 1), 1e-9, "a hero alone in the light");
            Check.Near(0.4, rules.PartyChance(false, false, Band(40, 0), 0, 1), 1e-9, "the dark still adds its share");
            Check.Near(0.1, rules.PartyChance(false, false, Band(0, 0), 0, 2), 1e-9, "two heroes start from the base");
        }

        public static void OneRollNeverSurprisesBothAndKeepsToTheChances()
        {
            var rules = new SurpriseRules();
            const int rolls = 200000;
            int party = 0, monsters = 0;
            var rng = new Rng(2024);
            var band = Band(15, 10);     // DD1's 26..50 band
            for (var i = 0; i < rolls; i++)
            {
                var side = rules.Roll(false, false, band, 0, 0, 4, rng);
                if (side == SurpriseSide.Party) party++;
                else if (side == SurpriseSide.Monsters) monsters++;
            }
            // 0.25 and 0.20 leave 0.55 for nobody: the chances are the probabilities
            Check.Near(0.25, party / (double)rolls, 0.004, "the party");
            Check.Near(0.20, monsters / (double)rolls, 0.004, "the monsters");
            var odds = rules.Odds(false, false, band, 0, 0);
            Check.Near(0.55, odds.None, 1e-9, "nobody's weight");
            Check.Near(1, odds.Sum, 1e-9, "the weights add up to 1");
        }

        public static void NobodyNeverWeighsLessThanAQuarter()
        {
            var rules = new SurpriseRules();
            // both at their maximum: 0.65 + 0.65 would leave less than nothing
            var odds = rules.Odds(false, false, Band(60, 60), 0, 0);
            Check.Near(0.25, odds.None, 1e-9, "nobody's weight");
            Check.Near(0.65 / 1.55, odds.PartyShare, 1e-9, "the party's share of the roll");
            Check.Near(0.65 / 1.55, odds.MonstersShare, 1e-9, "the monsters' share");
            const int rolls = 100000;
            int party = 0, monsters = 0;
            var rng = new Rng(8);
            for (var i = 0; i < rolls; i++)
            {
                var side = rules.Roll(false, false, Band(60, 60), 0, 0, 4, rng);
                if (side == SurpriseSide.Party) party++;
                else if (side == SurpriseSide.Monsters) monsters++;
            }
            Check.Near(0.65 / 1.55, party / (double)rolls, 0.005, "rolled: the party");
            Check.Near(0.65 / 1.55, monsters / (double)rolls, 0.005, "rolled: the monsters");

            // a known fight at full light: 0 and 0.5
            odds = rules.Odds(true, true, Band(0, 25), 0, 0);
            Check.Near(0, odds.Party, 1e-9, "known: the party's weight");
            Check.Near(0.5, odds.MonstersShare, 1e-9, "known, lit: half the time the monsters");
        }

        public static void TheMonstersHaveTheLastWord()
        {
            var ordinary = MonsterBattleModifier.Ordinary;
            Check.Equal(SurpriseSide.Party, SurpriseRules.Settle(SurpriseSide.Party, ordinary), "ordinary monsters surprise");
            Check.Equal(SurpriseSide.Monsters, SurpriseRules.Settle(SurpriseSide.Monsters, ordinary), "and are surprised");
            var boss = MonsterBattleModifier.Boss;
            Check.Equal(SurpriseSide.None, SurpriseRules.Settle(SurpriseSide.Party, boss), "a boss does not surprise");
            Check.Equal(SurpriseSide.None, SurpriseRules.Settle(SurpriseSide.Monsters, boss), "and is not surprised");
            var shambler = new MonsterBattleModifier { CanBeSurprised = false, AlwaysSurprise = true };
            Check.Equal(SurpriseSide.Party, SurpriseRules.Settle(SurpriseSide.None, shambler), "the Shambler always surprises");
            Check.Equal(SurpriseSide.Party, SurpriseRules.Settle(SurpriseSide.Monsters, shambler), "whatever was rolled");
            var sleeper = new MonsterBattleModifier { AlwaysBeSurprised = true };
            Check.Equal(SurpriseSide.Monsters, SurpriseRules.Settle(SurpriseSide.Party, sleeper), "always surprised");
            Check.Equal(SurpriseSide.None, SurpriseRules.Settle(SurpriseSide.None, null), "no monsters to ask");
        }

        public static void Dd1sMonstersSayWhoCanBeSurprised()
        {
            var shambler = MonsterBattleModifier.Load(Dd1.Files, "shambler_A");
            Check.True(shambler.CanSurprise && !shambler.CanBeSurprised && shambler.AlwaysSurprise && !shambler.AlwaysBeSurprised, "the Shambler: cannot be surprised, always surprises");
            Check.True(shambler.Torchlight != null && shambler.Torchlight[0] == 0 && shambler.Torchlight[1] == 0, "and holds the torch at nothing");
            foreach (var letter in new[] { "A", "B", "C" })
            {
                var collector = MonsterBattleModifier.Load(Dd1.Files, "collector_" + letter);
                Check.True(collector.CanSurprise && collector.CanBeSurprised && !collector.AlwaysSurprise && !collector.AlwaysBeSurprised && collector.Torchlight == null, "the Collector " + letter + " is ordinary");
            }
            // DD1's bosses of the four regions and its wandering-free ordinary monsters
            foreach (var boss in new[] { "necromancer_A", "prophet_B", "hag_C", "brigand_cannon_A", "swine_prince_B", "siren_C", "drowned_captain_A" })
            {
                var modifier = MonsterBattleModifier.Load(Dd1.Files, boss);
                Check.True(!modifier.CanSurprise && !modifier.CanBeSurprised, boss + " can neither surprise nor be surprised");
            }
            var bones = MonsterBattleModifier.Load(Dd1.Files, "skeleton_common_A");
            Check.True(bones.CanSurprise && bones.CanBeSurprised, "a bone rabble is ordinary");
            var nobody = MonsterBattleModifier.Load(Dd1.Files, "no_such_monster_A");
            Check.True(nobody.CanSurprise && nobody.CanBeSurprised && nobody.Torchlight == null, "a monster DD1 does not have is ordinary");
        }
    }
}
