using System.Collections.Generic;
using System.Linq;
using DD2Estate.Core;

namespace DD2Estate.CoreTests
{
    /// <summary>The rules behind DD1's results screens: outcome, the bag's cards and their gold, quirk and disease chances, experience.</summary>
    public static class RaidResultTests
    {
        private static readonly ItemDef Coin = new ItemDef { Type = ItemTypes.Gold, StackLimit = 1750 };
        private static readonly ItemDef Bread = new ItemDef { Type = ItemTypes.Provision, StackLimit = 12, BuyGold = 75, SellGold = 5, CanUnpack = true };
        private static readonly ItemDef Torch = new ItemDef { Type = ItemTypes.Supply, Id = "torch", StackLimit = 8, BuyGold = 75, SellGold = 5 };
        private static readonly ItemDef Log = new ItemDef { Type = ItemTypes.Supply, Id = "firewood", StackLimit = 1 };
        private static readonly ItemDef Jade = new ItemDef { Type = ItemTypes.Gem, Id = "jade", StackLimit = 5, SellGold = 375 };
        private static readonly ItemDef Crest = new ItemDef { Type = ItemTypes.Heirloom, Id = "crest", StackLimit = 12 };
        private static readonly ItemDef Relic = new ItemDef { Type = ItemTypes.QuestItem, Id = "relic", StackLimit = 1, SellGold = 999 };

        private static readonly int[] Thresholds = { 0, 2, 8, 14, 24, 36, 48 };

        // ---- the outcome (no DD1 needed: the stock values) ----

        public static void OutcomeFollowsTheGoalThenTheLiving()
        {
            var rules = RaidResultRules.Load(new NoDd1Files());
            Check.Equal(3, rules.MinHeroesForEscape, "DD1's stock value");
            Check.Equal(RaidOutcome.Victory, rules.Outcome(true, 4), "the goal met");
            Check.Equal(RaidOutcome.Victory, rules.Outcome(true, 1), "the goal met by the last hero standing");
            Check.Equal(RaidOutcome.Escape, rules.Outcome(false, 4), "abandoned by four");
            Check.Equal(RaidOutcome.Escape, rules.Outcome(false, 3), "abandoned by three");
            Check.Equal(RaidOutcome.Defeat, rules.Outcome(false, 2), "two came home");
            Check.Equal(RaidOutcome.Defeat, rules.Outcome(false, 0), "nobody came home");
        }

        public static void OutcomeRuleIsReadFromDd1()
        {
            var rules = RaidResultRules.Load(Dd1.Files);
            Check.Equal(3, rules.MinHeroesForEscape, "min_hero_count_for_escape");
            Check.Near(0.30, rules.NegStress0Success, 1e-9, "negStress0Success");
            Check.Near(0.50, rules.NegStress100Success, 1e-9, "negStress100Success");
            Check.Near(0.50, rules.PosStress0Success, 1e-9, "posStress0Success");
            Check.Near(0.40, rules.PosStress100Success, 1e-9, "posStress100Success");
            Check.Near(0.40, rules.NegStress0Failure, 1e-9, "negStress0Failure");
            Check.Near(0.70, rules.NegStress100Failure, 1e-9, "negStress100Failure");
            Check.Near(0.40, rules.PosStress0Failure, 1e-9, "posStress0Failure");
            Check.Near(0.25, rules.PosStress100Failure, 1e-9, "posStress100Failure");
            Check.Equal(2, rules.DiseaseMinResolveLevel, "disease_after_quest_min_resolve_level");
            Check.Near(0.32, rules.DiseaseMaxChance, 1e-9, "disease_max_chance");
            Check.Near(0.33, rules.DiseaseResistWeight, 1e-9, "disease_hero_disease_resist_weight");
            Check.Near(0.05, rules.DiseaseMinChance, 1e-9, "disease_after_quest_min_chance");
        }

        // ---- quirks and diseases ----

        public static void QuirkChancesRunFromCalmToStressed()
        {
            var rules = new RaidResultRules();
            Check.Near(0.30, rules.QuirkChance(false, true, 0), 1e-9, "negative, completed, calm");
            Check.Near(0.50, rules.QuirkChance(false, true, 1), 1e-9, "negative, completed, at 100");
            Check.Near(0.40, rules.QuirkChance(false, true, 0.5), 1e-9, "negative, completed, half way");
            Check.Near(0.50, rules.QuirkChance(true, true, 0), 1e-9, "positive, completed, calm");
            Check.Near(0.45, rules.QuirkChance(true, true, 0.5), 1e-9, "positive, completed, half way");
            Check.Near(0.40, rules.QuirkChance(false, false, 0), 1e-9, "negative, failed, calm");
            Check.Near(0.70, rules.QuirkChance(false, false, 1), 1e-9, "negative, failed, at 100");
            Check.Near(0.25, rules.QuirkChance(true, false, 1), 1e-9, "positive, failed, at 100");
            Check.Near(0.70, rules.QuirkChance(false, false, 1.8), 1e-9, "beyond 100 the chance stays");
            Check.Near(0.40, rules.QuirkChance(true, false, -1), 1e-9, "nothing below calm");
        }

        public static void DiseaseNeedsALevelAndGivesWayToResistance()
        {
            var rules = new RaidResultRules();
            Check.Near(0, rules.DiseaseChance(0, 0), 1e-9, "level 0");
            Check.Near(0, rules.DiseaseChance(1, 0), 1e-9, "level 1");
            Check.Near(0.32, rules.DiseaseChance(2, 0), 1e-9, "level 2, no resistance");
            Check.Near(0.32 - 0.3 * 0.33, rules.DiseaseChance(4, 0.3), 1e-9, "a resistance of 30%");
            Check.Near(0.05, rules.DiseaseChance(6, 1.0), 1e-9, "never under the lowest chance");
        }

        public static void TheRollIsTheSameForASeedAndKeepsToItsChances()
        {
            var rules = new RaidResultRules();
            var first = rules.Roll(true, 0.4, 3, 0.3, new Rng(7));
            var again = rules.Roll(true, 0.4, 3, 0.3, new Rng(7));
            Check.True(first.Negative == again.Negative && first.Positive == again.Positive && first.Disease == again.Disease, "one seed, one story");
            Check.True(first.Count <= RaidResultRules.QuirksPerHeroLimit, "never more than DD1's three a hero");

            const int rolls = 40000;
            int negative = 0, positive = 0, disease = 0, novice = 0;
            var rng = new Rng(20240229);
            for (var i = 0; i < rolls; i++)
            {
                var roll = rules.Roll(false, 1, 2, 0, rng);
                if (roll.Negative) negative++;
                if (roll.Positive) positive++;
                if (roll.Disease) disease++;
                if (rules.Roll(false, 1, 1, 0, rng).Disease) novice++;
            }
            Check.Near(0.70, negative / (double)rolls, 0.01, "negative quirks after a failed quest at 100 stress");
            Check.Near(0.25, positive / (double)rolls, 0.01, "positive quirks after a failed quest at 100 stress");
            Check.Near(0.32, disease / (double)rolls, 0.01, "diseases at level 2");
            Check.Equal(0, novice, "no disease below level 2");
        }

        // ---- experience ----

        public static void AQuestsOwnExperienceBeatsTheTable()
        {
            var apprentice = new[] { 0, 2, 3, 4, 4, 4 };
            var darkest = new[] { 0, 0, 0, 0, 0, 0 };
            Check.Equal(2, RaidResultRules.QuestExperience(0, apprentice, 1), "short, by the table");
            Check.Equal(3, RaidResultRules.QuestExperience(0, apprentice, 2), "medium, by the table");
            Check.Equal(4, RaidResultRules.QuestExperience(4, apprentice, 2), "a boss quest states 4 where the table's medium column has 3");
            Check.Equal(16, RaidResultRules.QuestExperience(16, darkest, 2), "a descent states 16; its row of the table is all zero");
            Check.Equal(0, RaidResultRules.QuestExperience(0, darkest, 2), "the table's row 6 alone pays nothing");
            Check.Equal(4, RaidResultRules.QuestExperience(0, apprentice, 9), "a length off the table takes its last column");
            Check.Equal(0, RaidResultRules.QuestExperience(0, null, 1), "no table, no experience");
        }

        public static void Dd1StatesTheExperienceOfItsPlotQuests()
        {
            var rewards = QuestRewardRules.Load(Dd1.Files);
            Check.Equal(4, rewards.Plot("plot_kill_necromancer_1").Reward.ResolveXp, "an apprentice boss");
            Check.Equal(8, rewards.Plot("plot_kill_hag_2").Reward.ResolveXp, "a veteran boss");
            Check.Equal(16, rewards.Plot("plot_kill_siren_3").Reward.ResolveXp, "a champion boss");
            for (var descent = 1; descent <= 4; descent++)
                Check.Equal(16, rewards.Plot("plot_darkest_dungeon_" + descent).Reward.ResolveXp, "descent " + descent);
            Check.Equal(2, rewards.Plot("plot_tutorial_crypts").Reward.ResolveXp, "the tutorial");
        }

        public static void LevelsAndTheBarsShare()
        {
            Check.Equal(0, RaidResultRules.Level(1, Thresholds, 6), "1 xp");
            Check.Equal(1, RaidResultRules.Level(2, Thresholds, 6), "2 xp");
            Check.Equal(2, RaidResultRules.Level(13, Thresholds, 6), "13 xp");
            Check.Equal(6, RaidResultRules.Level(500, Thresholds, 6), "the top");
            Check.Near(0.5, RaidResultRules.LevelShare(1, Thresholds, 6), 1e-9, "half way to level 1");
            Check.Near(0, RaidResultRules.LevelShare(8, Thresholds, 6), 1e-9, "a level just gained starts empty");
            Check.Near(5 / 6.0, RaidResultRules.LevelShare(13, Thresholds, 6), 1e-9, "5 of the 6 between levels 2 and 3");
            Check.Near(1, RaidResultRules.LevelShare(48, Thresholds, 6), 1e-9, "full at the top");
        }

        // ---- the bag on the screen ----

        private static Inventory Bag()
        {
            var bag = new Inventory(16);
            bag.Add(Torch, 3);
            bag.Add(Bread, 4);
            bag.Add(Coin, 1150);
            bag.Add(Jade, 1);
            bag.Add(Crest, 4);
            bag.Add(Log, 1);
            bag.Add(Relic, 1);
            bag.Add(Coin, 900);     // tops the purse up to 1750 and starts another
            return bag;
        }

        public static void TheHaulListsItsStacksInBagOrder()
        {
            var haul = Bag().Settle(null);
            Check.Equal("supply:torch x3 (15), provision x4 (20), gold x1750 (1750), gem:jade x1 (375), heirloom:crest x4 (0), supply:firewood x1 (0), gold x300 (300)",
                string.Join(", ", haul.Stacks.Select(s => s.ToString())), "stacks");
            Check.Equal(haul.Gold, haul.Stacks.Sum(s => s.Gold), "the stacks' worth is the haul's gold");
            Check.Equal(15 + 20 + 1750 + 375 + 300, haul.Gold, "gold");
        }

        public static void TreasureCardsUnpackFoodAndAddUp()
        {
            var haul = Bag().Settle(null);
            var cards = RaidResultRules.TreasureCards(haul);
            Check.Equal("supply:torch x3 (15), provision x1 (5), provision x1 (5), provision x1 (5), provision x1 (5), gold x1750 (1750), gem:jade x1 (375), gold x300 (300)",
                string.Join(", ", cards.Select(c => c.ToString())), "cards: food a ration a card, no heirlooms, nothing that is worth nothing");
            Check.Equal(haul.Gold, cards.Sum(c => c.Gold), "the cards add up to the haul");

            var heirlooms = RaidResultRules.HeirloomCards(haul);
            Check.True(heirlooms.Count == 1 && heirlooms[0].Item == Crest && heirlooms[0].Amount == 4 && heirlooms[0].Gold == 0, "one card a stack of heirlooms");
        }

        public static void ARetreatKeepsAQuarterOfTheSupplies()
        {
            // DD1's quest_fail_keep_rates: gold, gems and heirlooms whole, food and supplies a quarter, rounded down a stack
            var haul = Bag().Settle(type => type == ItemTypes.Provision || type == ItemTypes.Supply ? 0.25 : type == ItemTypes.QuestItem ? 0 : 1);
            var cards = RaidResultRules.TreasureCards(haul);
            Check.Equal("provision x1 (5), gold x1750 (1750), gem:jade x1 (375), gold x300 (300)", string.Join(", ", cards.Select(c => c.ToString())), "a quarter of 3 torches is none, of 4 rations one");
            Check.Equal(2430, haul.Gold, "gold");
            Check.Equal(4, haul.Heirlooms["crest"], "heirlooms are kept whole");

            var lost = Bag().Settle(type => 0);
            Check.True(lost.Stacks.Count == 0 && RaidResultRules.TreasureCards(lost).Count == 0 && RaidResultRules.HeirloomCards(lost).Count == 0, "a lost party brings nothing home");
            Check.True(RaidResultRules.TreasureCards(null).Count == 0, "no haul, no cards");
        }

        public static void GridCentresAShortRowAndSqueezesALongOne()
        {
            // the rewards row: 8 columns of 80, centred: three cards start 200 in
            Slot(0, 3, 8, false, out var x, out var y);
            Check.Near(200, x, 1e-9, "first of three on eight columns");
            Slot(2, 3, 8, false, out x, out y);
            Check.Near(360, x, 1e-9, "third of three");
            // a plain grid wraps
            Slot(9, 10, 8, false, out x, out y);
            Check.True(x == 240 + 80 && y == 160, "the second row holds two and is centred: " + x + ", " + y);

            // the bag's rows: one row of 6 columns
            Slot(0, 6, 6, true, out x, out y);
            Check.Near(0, x, 1e-9, "a full row starts at the grid's start");
            Slot(5, 6, 6, true, out x, out y);
            Check.Near(400, x, 1e-9, "its last card");
            Slot(0, 2, 6, true, out x, out y);
            Check.Near(160, x, 1e-9, "two cards are centred");
            Slot(11, 12, 6, true, out x, out y);
            Check.True(x == 11 * 40 && y == 0, "twelve cards keep to one row at half the pitch: " + x + ", " + y);
            Slot(15, 16, 6, true, out x, out y);
            Check.Near(15 * 30, x, 1e-9, "sixteen at 30 px");
        }

        private static void Slot(int index, int count, int columns, bool oneRow, out double x, out double y)
        {
            RaidResultRules.GridSlot(index, count, columns, 80, 160, true, oneRow, out x, out y);
        }

        // ---- DD1's pictures of a stack ----

        public static void StackPicturesFollowDd1sThresholds()
        {
            var food = new ItemDef { Type = ItemTypes.Provision, StackLimit = 12, IconVariants = 4, IconThresholds = new[] { 0, 5, 11, 14 } };
            var gold = new ItemDef { Type = ItemTypes.Gold, StackLimit = 1750, IconVariants = 4, IconThresholds = new[] { 250, 500, 750, 1000 } };
            Check.Equal("panels/icons_equip/provision/inv_provision+_1.png", food.IconPath(1), "one ration");
            Check.Equal("panels/icons_equip/provision/inv_provision+_1.png", food.IconPath(5), "five");
            Check.Equal("panels/icons_equip/provision/inv_provision+_2.png", food.IconPath(6), "six");
            Check.Equal("panels/icons_equip/provision/inv_provision+_3.png", food.IconPath(12), "a full stack");
            Check.Equal("panels/icons_equip/gold/inv_gold+_0.png", gold.IconPath(250), "a small purse");
            Check.Equal("panels/icons_equip/gold/inv_gold+_1.png", gold.IconPath(251), "past the first threshold");
            Check.Equal("panels/icons_equip/gold/inv_gold+_3.png", gold.IconPath(1150), "past them all but the last");
            Check.Equal("panels/icons_equip/gold/inv_gold+_3.png", gold.IconPath(3000), "a quest's reward");
        }

        public static void Dd1SaysWhatIsUnpackedAndWhichPicture()
        {
            var items = ItemCatalog.Load(Dd1.Files);
            Check.True(items.Food.CanUnpack, "food is laid out a ration a card");
            Check.True(!items.Gold.CanUnpack && !items.Find("torch").CanUnpack && !items.Find("ruby").CanUnpack, "nothing else is");
            Check.Equal("0, 5, 11, 14", string.Join(", ", items.Food.IconThresholds ?? new int[0]), "food's steps");
            Check.Equal("250, 500, 750, 1000", string.Join(", ", items.Gold.IconThresholds ?? new int[0]), "gold's steps");
            Check.True(items.Find("torch").IconThresholds == null, "a torch has one picture");
            foreach (var amount in new[] { 1, 5, 6, 12 }) Check.True(Dd1.Files.Exists(items.Food.IconPath(amount)), "food picture for " + amount);
            foreach (var amount in new[] { 1, 300, 600, 900, 1750 }) Check.True(Dd1.Files.Exists(items.Gold.IconPath(amount)), "gold picture for " + amount);

            var none = ItemCatalog.Load(new NoDd1Files());
            Check.True(none.Food.CanUnpack && !none.Gold.CanUnpack, "without the file: DD1's stock answer");
        }

        public static void Dd1sSellValuesAreWhatTheScreenCounts()
        {
            var items = ItemCatalog.Load(Dd1.Files);
            var expected = new Dictionary<string, int>
            {
                { "torch", 5 }, { "shovel", 25 }, { "skeleton_key", 20 }, { "medicinal_herbs", 20 }, { "laudanum", 20 }, { "bandage", 15 }, { "antivenom", 15 }, { "holy_water", 15 },
                { "jade", 375 }, { "citrine", 250 }, { "onyx", 500 }, { "emerald", 750 }, { "sapphire", 1000 }, { "ruby", 1250 }
            };
            foreach (var pair in expected) Check.Equal(pair.Value, items.Find(pair.Key).SellGold, pair.Key);
            Check.Equal(5, items.Food.SellGold, "food");
            Check.Equal(0, items.Find("firewood").SellGold, "firewood is worth nothing and is not shown");

            // the owner's Escape: antivenom, two rations, a torch, 1,150 gold and a jade came to 1,555
            var bag = new Inventory(16);
            bag.Add(items.Find("antivenom"), 1);
            bag.Add(items.Food, 2);
            bag.Add(items.Find("torch"), 1);
            bag.Add(items.Gold, 1150);
            bag.Add(items.Find("jade"), 1);
            var cards = RaidResultRules.TreasureCards(bag.Settle(null));
            Check.Equal(6, cards.Count, "six cards");
            Check.Equal(1555, cards.Sum(c => c.Gold), "DD1's total");
        }
    }
}
